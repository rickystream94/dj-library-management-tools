namespace LibTools4DJs.MixedInKey.Models
{
    internal sealed class MikCollection(string? parentId, string name, bool isFolder) : IEquatable<MikCollection>
    {
        public string? ParentId { get; } = parentId;

        public string Name { get; } = name;

        public bool IsFolder { get; } = isFolder;

        public override int GetHashCode() => HashCode.Combine(this.ParentId, this.Name, this.IsFolder);

        public override bool Equals(object? obj) => Equals(obj as MikCollection);

        public bool Equals(MikCollection? other) =>
            other is not null &&
            string.Equals(this.ParentId, other.ParentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
            this.IsFolder == other.IsFolder;
    }
}
