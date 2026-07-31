using System;

namespace VvvvPluginAnalyzer.Models
{
    public class NugetDependency : IEquatable<NugetDependency>
    {
        public string Id { get; set; } = "";
        public string Location { get; set; } = "";
        public string Version { get; set; } = "";

        public bool Equals(NugetDependency? other)
        {
            return other != null && Location == other.Location && Version == other.Version;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Location, Version);
        }
    }
}