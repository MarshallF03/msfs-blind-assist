// A synthetic BGL: header, one SceneryObject section, one subsection, three LibraryObject records.
// Never a payware file. Layout as measured on three Community packages, 2026-09-06.
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class BglPlacementReaderTests
{
    internal static byte[] BuildBgl(params (double lat, double lon, double hdg, Guid guid)[] objs)
    {
        const int header = 0x38, sectionEntry = 20, subEntry = 16, rec = 64;
        int sectionTable = header, subTable = sectionTable + sectionEntry, data = subTable + subEntry;
        var b = new byte[data + objs.Length * rec];
        void U32(int at, uint v) => BitConverter.TryWriteBytes(b.AsSpan(at, 4), v);
        void U16(int at, ushort v) => BitConverter.TryWriteBytes(b.AsSpan(at, 2), v);

        U32(0x00, 0x19920201); U32(0x14, 1);
        U32(sectionTable + 0, 0x25); U32(sectionTable + 4, 1); U32(sectionTable + 8, 1);
        U32(sectionTable + 12, (uint)subTable); U32(sectionTable + 16, subEntry);
        U32(subTable + 0, 0); U32(subTable + 4, (uint)objs.Length); U32(subTable + 8, (uint)data); U32(subTable + 12, (uint)(objs.Length * rec));

        int p = data;
        foreach (var (lat, lon, hdg, guid) in objs)
        {
            U16(p, 0x0B); U16(p + 2, rec);
            U32(p + 4, (uint)Math.Round((lon + 180.0) * (3.0 * (1 << 28)) / 360.0));
            U32(p + 8, (uint)Math.Round((90.0 - lat) * (2.0 * (1 << 28)) / 180.0));
            U16(p + 22, (ushort)Math.Round(hdg * 65536.0 / 360.0));
            guid.ToByteArray().CopyTo(b, p + 44);
            p += rec;
        }
        return b;
    }

    [Fact]
    public void Reads_position_heading_and_guid_of_each_library_object()
    {
        var g1 = Guid.Parse("416f6b5f-f52e-4744-858a-29067c13cdb0");
        var g2 = Guid.Parse("82cb66da-9f5b-4116-a774-a6ce91702279");
        var bgl = BuildBgl((47.27064, -122.57373, 277.0, g1), (47.26765, -122.57497, 7.0, g2));
        var placed = BglPlacementReader.Read(bgl);
        Assert.Equal(2, placed.Count);
        Assert.InRange(placed[0].Lat, 47.27063, 47.27065);
        Assert.InRange(placed[0].Lon, -122.57374, -122.57372);
        Assert.InRange(placed[0].HeadingDeg, 276.9, 277.1);
        Assert.Equal(g1, placed[0].ModelGuid);
        Assert.Equal(g2, placed[1].ModelGuid);
    }

    [Fact]
    public void Non_bgl_and_truncated_input_yield_empty_or_partial_never_throw()
    {
        Assert.Empty(BglPlacementReader.Read(new byte[] { 1, 2, 3 }));
        Assert.Empty(BglPlacementReader.Read(Array.Empty<byte>()));
        var bgl = BuildBgl((1, 1, 0, Guid.NewGuid()), (2, 2, 0, Guid.NewGuid()));
        var cut = bgl.AsSpan(0, bgl.Length - 40).ToArray();          // second record truncated
        Assert.Single(BglPlacementReader.Read(cut));
    }

    [Fact]
    public void Records_that_are_not_library_objects_are_skipped()
    {
        var bgl = BuildBgl((1, 1, 0, Guid.NewGuid()));
        BitConverter.TryWriteBytes(bgl.AsSpan(0x38 + 20 + 16, 2), (ushort)0x0E);   // flip the record id
        Assert.Empty(BglPlacementReader.Read(bgl));
    }
}
