using System.Buffers.Binary;

namespace MSFSBlindAssist.Services.SceneryIndex;

public readonly record struct ScenePlacement(double Lat, double Lon, double HeadingDeg, Guid ModelGuid);

/// <summary>
/// Reads LibraryObject placements out of an MSFS scenery BGL. Layout measured 2026-09-06 on
/// Orbx KTIW, imaginesim KATL and Axonos KJAC (see the plan/spec). Bounds-checked at every
/// step: a truncated or foreign file yields whatever parsed cleanly, never an exception.
/// </summary>
public static class BglPlacementReader
{
    private const uint Magic = 0x19920201;
    private const int HeaderSize = 0x38, SectionEntrySize = 20;
    private const uint SceneryObjectSection = 0x25;
    private const ushort LibraryObjectId = 0x0B;
    private const int LibraryObjectSize = 64;
    private const double LonScale = 360.0 / (3.0 * (1 << 28));
    private const double LatScale = 180.0 / (2.0 * (1 << 28));

    public static List<ScenePlacement> Read(ReadOnlySpan<byte> b)
    {
        var result = new List<ScenePlacement>();
        if (b.Length < HeaderSize || U32(b, 0) != Magic) return result;
        uint sections = U32(b, 0x14);
        for (uint s = 0; s < sections; s++)
        {
            int e = HeaderSize + (int)s * SectionEntrySize;
            if (e + SectionEntrySize > b.Length) break;
            if (U32(b, e) != SceneryObjectSection) continue;
            uint subCount = U32(b, e + 8), subOff = U32(b, e + 12), subSize = U32(b, e + 16);
            if (subCount == 0 || subSize < 16 || subOff >= b.Length || (long)subOff + subSize > b.Length) continue;
            int subEntry = (int)(subSize / subCount);
            if (subEntry < 16) continue;
            for (uint i = 0; i < subCount; i++)
            {
                int so = (int)subOff + (int)i * subEntry;
                if ((long)so + subEntry > b.Length) continue;
                uint dataOff = U32(b, so + subEntry - 8), dataSize = U32(b, so + subEntry - 4);
                if (dataOff >= b.Length) continue;
                long end = Math.Min((long)dataOff + dataSize, b.Length);
                int p = (int)dataOff;
                while (p + 4 <= end)
                {
                    ushort id = U16(b, p), size = U16(b, p + 2);
                    if (size < 4 || p + size > end) break;
                    if (id == LibraryObjectId && size >= LibraryObjectSize)
                    {
                        double lon = U32(b, p + 4) * LonScale - 180.0;
                        double lat = 90.0 - U32(b, p + 8) * LatScale;
                        double hdg = U16(b, p + 22) * (360.0 / 65536.0);
                        var guid = new Guid(b.Slice(p + 44, 16));
                        result.Add(new ScenePlacement(lat, lon, hdg, guid));
                    }
                    p += size;
                }
            }
        }
        return result;
    }

    private static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at, 4));
    private static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(at, 2));
}
