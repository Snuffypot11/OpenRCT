using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenCMTools.Core.Settings;

namespace OpenCMTools.Core.Updates
{
    public sealed class UpdateCheckResult
    {
        public bool IsConfigured { get; set; }
        public bool IsSuccess { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public string Repository { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseName { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public DateTime? PublishedAtUtc { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Fork-friendly, manual-trigger GitHub Release update checker.
    /// Contains zero hardcoded repository URLs in source code: resolves the target repository from
    /// user Settings first, then from build-time AssemblyMetadata("UpdateRepository") injected by CI.
    /// </summary>
    public static class GitHubUpdateChecker
    {
        private static readonly Regex OwnerRepoRegex = new Regex(
            @"^(?:https?://(?:www\.)?github\.com/)?(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+?)(?:\.git|/(?:releases|tags|tree|blob).*)?/?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [DataContract]
        private sealed class GitHubReleaseDto
        {
            [DataMember(Name = "tag_name")]
            public string? TagName { get; set; }

            [DataMember(Name = "name")]
            public string? Name { get; set; }

            [DataMember(Name = "body")]
            public string? Body { get; set; }

            [DataMember(Name = "html_url")]
            public string? HtmlUrl { get; set; }

            [DataMember(Name = "published_at")]
            public string? PublishedAt { get; set; }

            [DataMember(Name = "prerelease")]
            public bool Prerelease { get; set; }
        }

        public static string GetCurrentVersionString(Assembly? assembly = null)
        {
            var asm = assembly ?? typeof(GitHubUpdateChecker).Assembly;
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                var plusIdx = infoVer!.IndexOf('+');
                return plusIdx > 0 ? infoVer.Substring(0, plusIdx) : infoVer;
            }
            var v = asm.GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}" : "0.0.0";
        }

        public static string? GetBuildDefaultRepository(Assembly? assembly = null)
        {
            var asm = assembly ?? typeof(GitHubUpdateChecker).Assembly;
            var meta = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, "UpdateRepository", StringComparison.OrdinalIgnoreCase));
            return TryNormalizeRepository(meta?.Value, out var normalized) ? normalized : null;
        }

        public static string? ResolveEffectiveRepository(OpenCMToolsSettings? settings, Assembly? assembly = null)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.UpdateRepository))
            {
                return TryNormalizeRepository(settings.UpdateRepository, out var userRepo) ? userRepo : null;
            }
            return GetBuildDefaultRepository(assembly);
        }

        public static bool TryNormalizeRepository(string? input, out string normalizedOwnerRepo)
        {
            normalizedOwnerRepo = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var trimmed = input!.Trim();
            var match = OwnerRepoRegex.Match(trimmed);
            if (!match.Success) return false;

            var owner = match.Groups["owner"].Value;
            var repo = match.Groups["repo"].Value;
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || owner.Contains("..") || repo.Contains(".."))
            {
                return false;
            }

            normalizedOwnerRepo = $"{owner}/{repo}";
            return true;
        }

        public static bool TryParseSemVer(string? versionText, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(versionText)) return false;

            var cleaned = versionText!.Trim().TrimStart('v', 'V');
            var dashIdx = cleaned.IndexOf('-');
            if (dashIdx > 0) cleaned = cleaned.Substring(0, dashIdx);
            var plusIdx = cleaned.IndexOf('+');
            if (plusIdx > 0) cleaned = cleaned.Substring(0, plusIdx);

            var parts = cleaned.Split('.');
            if (parts.Length == 2) cleaned += ".0";
            if (Version.TryParse(cleaned, out var parsed) && parsed != null)
            {
                version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
                return true;
            }
            return false;
        }

        public static bool IsNewerVersion(string? currentVersionText, string? latestVersionText)
        {
            if (!TryParseSemVer(currentVersionText, out var cur) || !TryParseSemVer(latestVersionText, out var latest))
            {
                return false;
            }
            return latest > cur;
        }

        public static UpdateCheckResult ParseReleaseResponseJson(
            string json,
            string repository,
            string currentVersion)
        {
            var serializer = new DataContractJsonSerializer(typeof(GitHubReleaseDto));
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var dto = serializer.ReadObject(ms) as GitHubReleaseDto;
            if (dto == null || string.IsNullOrWhiteSpace(dto.TagName))
            {
                return new UpdateCheckResult
                {
                    IsConfigured = true,
                    IsSuccess = false,
                    Repository = repository,
                    CurrentVersion = currentVersion,
                    Message = "The GitHub Releases API returned an unexpected payload."
                };
            }

            var latest = dto.TagName!.Trim();
            var isNewer = IsNewerVersion(currentVersion, latest);
            DateTime? published = null;
            if (!string.IsNullOrWhiteSpace(dto.PublishedAt) && DateTime.TryParse(dto.PublishedAt, out var dt))
            {
                published = dt.ToUniversalTime();
            }

            return new UpdateCheckResult
            {
                IsConfigured = true,
                IsSuccess = true,
                IsUpdateAvailable = isNewer,
                Repository = repository,
                CurrentVersion = currentVersion,
                LatestVersion = latest,
                ReleaseName = string.IsNullOrWhiteSpace(dto.Name) ? latest : dto.Name!,
                ReleaseNotes = dto.Body ?? string.Empty,
                ReleaseUrl = !string.IsNullOrWhiteSpace(dto.HtmlUrl) ? dto.HtmlUrl! : $"https://github.com/{repository}/releases/latest",
                PublishedAtUtc = published,
                Message = isNewer
                    ? $"A new release ({latest}) is available!"
                    : $"You are running the latest release (v{currentVersion})."
            };
        }

        public static async Task<UpdateCheckResult> CheckForUpdatesAsync(
            OpenCMToolsSettings settings,
            Assembly? appAssembly = null,
            CancellationToken ct = default)
        {
            var currentVer = GetCurrentVersionString(appAssembly);

            if (settings != null && !settings.EnableUpdateChecks)
            {
                return new UpdateCheckResult
                {
                    IsConfigured = false,
                    IsSuccess = false,
                    CurrentVersion = currentVer,
                    Message = "Update checks are disabled in OpenCMTools Settings."
                };
            }

            var repo = ResolveEffectiveRepository(settings, appAssembly);
            if (string.IsNullOrWhiteSpace(repo))
            {
                return new UpdateCheckResult
                {
                    IsConfigured = false,
                    IsSuccess = false,
                    CurrentVersion = currentVer,
                    Message = "No GitHub update repository is configured. Open Settings -> Updates and enter an owner/repo or GitHub URL (for example, your fork or upstream repository)."
                };
            }

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }

            var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"OpenCMTools/{currentVer}");
                http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

                using var resp = await http.GetAsync(apiUrl, ct).ConfigureAwait(false);
                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    return new UpdateCheckResult
                    {
                        IsConfigured = true,
                        IsSuccess = true,
                        IsUpdateAvailable = false,
                        Repository = repo!,
                        CurrentVersion = currentVer,
                        ReleaseUrl = $"https://github.com/{repo}/releases",
                        Message = $"No published GitHub Releases were found yet for '{repo}'. You are on v{currentVer}."
                    };
                }

                if (!resp.IsSuccessStatusCode)
                {
                    return new UpdateCheckResult
                    {
                        IsConfigured = true,
                        IsSuccess = false,
                        Repository = repo!,
                        CurrentVersion = currentVer,
                        Message = $"GitHub API returned HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}) for '{repo}'."
                    };
                }

                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseReleaseResponseJson(json, repo!, currentVer);
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    IsConfigured = true,
                    IsSuccess = false,
                    Repository = repo!,
                    CurrentVersion = currentVer,
                    Message = $"Could not reach GitHub ({repo}): {ex.Message}"
                };
            }
        }
    }
}
