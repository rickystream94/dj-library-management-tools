namespace LibTools4DJs.MixedInKey.Models
{
    internal sealed class MikSongCollectionMembership(string songId, string collectionId, int sequence)
    {
        public string SongId { get; } = songId;

        public string CollectionId { get; } = collectionId;

        public int Sequence { get; } = sequence;
    }
}
