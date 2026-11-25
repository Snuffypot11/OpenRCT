using System;
using System.Runtime.Serialization;

namespace OpenRCT.Core.Models
{
    /// <summary>
    /// Represents a managed endpoint selected in MECM (single, multi-select, or expanded from a Device Collection).
    /// </summary>
    [DataContract]
    public sealed class TargetDevice
    {
        [DataMember(Order = 1)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string? Fqdn { get; set; }

        [DataMember(Order = 3)]
        public string? ResourceId { get; set; }

        [DataMember(Order = 4)]
        public string? Domain { get; set; }

        [DataMember(Order = 5)]
        public string? SiteCode { get; set; }

        [DataMember(Order = 6)]
        public string? SmsProviderServer { get; set; }

        [DataMember(Order = 7)]
        public string? CollectionId { get; set; }

        [DataMember(Order = 8)]
        public string? CollectionName { get; set; }

        /// <summary>
        /// Best network address for connecting to the target (FQDN if available, otherwise NetBIOS Name).
        /// </summary>
        public string ConnectAddress =>
            !string.IsNullOrWhiteSpace(Fqdn) ? Fqdn!.Trim() : Name.Trim();

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Name) ? (Fqdn ?? string.Empty) : Name;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Context passed from the MECM Admin Console or standalone launcher into any tool or dashboard.
    /// </summary>
    [DataContract]
    public sealed class ToolInvocationContext
    {
        [DataMember(Order = 1)]
        public string ActionKey { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string? SmsProviderServer { get; set; }

        [DataMember(Order = 3)]
        public string? SiteCode { get; set; }

        [DataMember(Order = 4)]
        public string? CollectionId { get; set; }

        [DataMember(Order = 5)]
        public string? CollectionName { get; set; }

        [DataMember(Order = 6)]
        public TargetDevice[] Devices { get; set; } = Array.Empty<TargetDevice>();

        public bool IsCollectionScope => !string.IsNullOrWhiteSpace(CollectionId) && Devices.Length == 0;
    }
}
