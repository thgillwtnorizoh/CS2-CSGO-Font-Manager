using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSGO_Font_Manager
{
    internal sealed class FontEncodingInfo
    {
        public bool IsSupportedContainer { get; set; }
        public bool IsUnicode { get; set; }
        public bool HasUnicodeBmp { get; set; }
        public bool HasUnicodeFull { get; set; }
        public bool HasWindowsSymbol { get; set; }
        public bool CanAutoConvertSymbolToUnicode { get; set; }
        public int BasicLatinCoverage { get; set; }
        public string EncodingDescription { get; set; }
        public string Detail { get; set; }
    }

    internal static class FontEncodingInspector
    {
        private sealed class TableRecord
        {
            public string Tag;
            public uint Checksum;
            public uint Offset;
            public uint Length;
        }

        private sealed class CmapRecord
        {
            public ushort PlatformId;
            public ushort EncodingId;
            public uint Offset;
            public ushort Format;
        }

        public static FontEncodingInfo Inspect(string fontFilePath)
        {
            FontEncodingInfo info = new FontEncodingInfo
            {
                EncodingDescription = "Unknown",
                Detail = "The font encoding could not be determined."
            };

            try
            {
                byte[] data = File.ReadAllBytes(fontFilePath);
                if (data.Length < 12) return info;

                if (ReadTag(data, 0) == "ttcf")
                {
                    info.Detail = "TrueType Collections are not currently inspected.";
                    return info;
                }

                List<TableRecord> tables = ReadTableDirectory(data);
                TableRecord cmap = tables.FirstOrDefault(t => t.Tag == "cmap");
                if (cmap == null || !RangeInside(data, cmap.Offset, cmap.Length))
                {
                    info.Detail = "No readable cmap table was found.";
                    return info;
                }

                info.IsSupportedContainer = true;
                List<CmapRecord> records = ReadCmapRecords(data, cmap);

                bool windowsBmp = records.Any(r => r.PlatformId == 3 && r.EncodingId == 1 && r.Format == 4);
                bool windowsFull = records.Any(r => r.PlatformId == 3 && r.EncodingId == 10 && (r.Format == 12 || r.Format == 13));
                bool unicodePlatformBmp = records.Any(r => r.PlatformId == 0 && (r.Format == 4 || r.Format == 6));
                bool unicodePlatformFull = records.Any(r => r.PlatformId == 0 && (r.Format == 12 || r.Format == 13));
                bool symbol = records.Any(r => r.PlatformId == 3 && r.EncodingId == 0);

                info.HasUnicodeBmp = windowsBmp || unicodePlatformBmp;
                info.HasUnicodeFull = windowsFull || unicodePlatformFull;
                info.HasWindowsSymbol = symbol;
                info.IsUnicode = info.HasUnicodeBmp || info.HasUnicodeFull;
                info.CanAutoConvertSymbolToUnicode = !info.IsUnicode &&
                    records.Any(r => r.PlatformId == 3 && r.EncodingId == 0 && r.Format == 4);

                CmapRecord coverageRecord = records.FirstOrDefault(r => r.PlatformId == 3 && r.EncodingId == 10 && r.Format == 12)
                    ?? records.FirstOrDefault(r => r.PlatformId == 3 && r.EncodingId == 1 && r.Format == 4)
                    ?? records.FirstOrDefault(r => r.PlatformId == 0 && r.Format == 12)
                    ?? records.FirstOrDefault(r => r.PlatformId == 0 && r.Format == 4);

                if (coverageRecord != null)
                    info.BasicLatinCoverage = CountBasicLatinCoverage(data, cmap, coverageRecord);

                if (info.HasUnicodeFull)
                    info.EncodingDescription = info.HasUnicodeBmp ? "Unicode full repertoire + BMP" : "Unicode full repertoire";
                else if (info.HasUnicodeBmp)
                    info.EncodingDescription = "Unicode BMP";
                else if (symbol)
                    info.EncodingDescription = "Windows Symbol (legacy, non-Unicode)";
                else
                {
                    CmapRecord legacyWindows = records.FirstOrDefault(r => r.PlatformId == 3 && r.EncodingId >= 2 && r.EncodingId <= 6);
                    if (legacyWindows != null)
                        info.EncodingDescription = WindowsEncodingName(legacyWindows.EncodingId) + " (legacy, non-Unicode)";
                    else
                        info.EncodingDescription = "Legacy/non-Unicode cmap";
                }

                StringBuilder details = new StringBuilder();
                details.Append("cmap records: ");
                for (int i = 0; i < records.Count; i++)
                {
                    if (i > 0) details.Append(", ");
                    details.Append(records[i].PlatformId).Append('/').Append(records[i].EncodingId)
                        .Append(" format ").Append(records[i].Format);
                }
                if (info.IsUnicode)
                    details.Append("; Basic Latin coverage: ").Append(info.BasicLatinCoverage).Append("/95");
                info.Detail = details.ToString();
            }
            catch (Exception exception)
            {
                info.Detail = exception.Message;
            }

            return info;
        }

        public static bool TryCreateUnicodeBmpCopy(string sourcePath, string destinationPath, out string error)
        {
            error = null;
            try
            {
                byte[] source = File.ReadAllBytes(sourcePath);
                List<TableRecord> tables = ReadTableDirectory(source);
                TableRecord cmap = tables.FirstOrDefault(t => t.Tag == "cmap");
                if (cmap == null || !RangeInside(source, cmap.Offset, cmap.Length))
                {
                    error = "The font has no readable cmap table.";
                    return false;
                }

                List<CmapRecord> records = ReadCmapRecords(source, cmap);
                CmapRecord symbolRecord = records.FirstOrDefault(r => r.PlatformId == 3 && r.EncodingId == 0 && r.Format == 4);
                if (symbolRecord == null)
                {
                    error = "Automatic conversion currently supports Windows Symbol format-4 fonts only.";
                    return false;
                }

                Dictionary<ushort, ushort> mappings = ReadSymbolMappingsAsUnicodeBmp(source, cmap, symbolRecord);
                if (mappings.Count == 0)
                {
                    error = "No convertible Windows Symbol mappings were found.";
                    return false;
                }

                byte[] unicodeFormat4 = BuildFormat4(mappings);
                byte[] originalCmap = Slice(source, (int)cmap.Offset, (int)cmap.Length);
                byte[] newCmap = AddUnicodeBmpRecord(originalCmap, unicodeFormat4);
                byte[] rebuilt = RebuildSfnt(source, tables, newCmap);

                string directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(destinationPath, rebuilt);

                FontEncodingInfo check = Inspect(destinationPath);
                if (!check.IsUnicode || !check.HasUnicodeBmp)
                {
                    try { File.Delete(destinationPath); } catch { }
                    error = "The converted font failed Unicode cmap validation.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static List<TableRecord> ReadTableDirectory(byte[] data)
        {
            if (data.Length < 12) throw new InvalidDataException("The font header is truncated.");
            ushort numTables = ReadUInt16(data, 4);
            if (12 + numTables * 16 > data.Length) throw new InvalidDataException("The font table directory is truncated.");

            List<TableRecord> tables = new List<TableRecord>();
            for (int i = 0; i < numTables; i++)
            {
                int p = 12 + i * 16;
                TableRecord record = new TableRecord
                {
                    Tag = ReadTag(data, p),
                    Checksum = ReadUInt32(data, p + 4),
                    Offset = ReadUInt32(data, p + 8),
                    Length = ReadUInt32(data, p + 12)
                };
                if (!RangeInside(data, record.Offset, record.Length))
                    throw new InvalidDataException("Font table " + record.Tag + " is outside the file.");
                tables.Add(record);
            }
            return tables;
        }

        private static List<CmapRecord> ReadCmapRecords(byte[] data, TableRecord cmap)
        {
            int start = (int)cmap.Offset;
            if (cmap.Length < 4) throw new InvalidDataException("The cmap table is truncated.");
            ushort count = ReadUInt16(data, start + 2);
            if (4 + count * 8 > cmap.Length) throw new InvalidDataException("The cmap encoding records are truncated.");

            List<CmapRecord> records = new List<CmapRecord>();
            for (int i = 0; i < count; i++)
            {
                int p = start + 4 + i * 8;
                uint offset = ReadUInt32(data, p + 4);
                if (offset + 2 > cmap.Length) continue;
                records.Add(new CmapRecord
                {
                    PlatformId = ReadUInt16(data, p),
                    EncodingId = ReadUInt16(data, p + 2),
                    Offset = offset,
                    Format = ReadUInt16(data, start + (int)offset)
                });
            }
            return records;
        }

        private static int CountBasicLatinCoverage(byte[] data, TableRecord cmap, CmapRecord record)
        {
            int count = 0;
            for (uint code = 0x20; code <= 0x7E; code++)
            {
                if (GetGlyphId(data, (int)cmap.Offset + (int)record.Offset, record.Format, code) != 0)
                    count++;
            }
            return count;
        }

        private static ushort GetGlyphId(byte[] data, int subtable, ushort format, uint codePoint)
        {
            if (format == 4 && codePoint <= 0xFFFF)
                return GetGlyphIdFormat4(data, subtable, (ushort)codePoint);
            if (format == 12)
                return GetGlyphIdFormat12(data, subtable, codePoint);
            return 0;
        }

        private static ushort GetGlyphIdFormat4(byte[] data, int subtable, ushort codePoint)
        {
            ushort length = ReadUInt16(data, subtable + 2);
            int end = subtable + length;
            ushort segCount = (ushort)(ReadUInt16(data, subtable + 6) / 2);
            int endCodes = subtable + 14;
            int startCodes = endCodes + segCount * 2 + 2;
            int deltas = startCodes + segCount * 2;
            int rangeOffsets = deltas + segCount * 2;

            for (int i = 0; i < segCount; i++)
            {
                ushort segEnd = ReadUInt16(data, endCodes + i * 2);
                if (codePoint > segEnd) continue;
                ushort segStart = ReadUInt16(data, startCodes + i * 2);
                if (codePoint < segStart) return 0;

                short delta = ReadInt16(data, deltas + i * 2);
                ushort rangeOffset = ReadUInt16(data, rangeOffsets + i * 2);
                if (rangeOffset == 0)
                    return (ushort)((codePoint + delta) & 0xFFFF);

                int rangeAddress = rangeOffsets + i * 2;
                int glyphAddress = rangeAddress + rangeOffset + (codePoint - segStart) * 2;
                if (glyphAddress < subtable || glyphAddress + 2 > end || glyphAddress + 2 > data.Length) return 0;
                ushort glyph = ReadUInt16(data, glyphAddress);
                if (glyph == 0) return 0;
                return (ushort)((glyph + delta) & 0xFFFF);
            }
            return 0;
        }

        private static ushort GetGlyphIdFormat12(byte[] data, int subtable, uint codePoint)
        {
            uint length = ReadUInt32(data, subtable + 4);
            uint groups = ReadUInt32(data, subtable + 12);
            int end = subtable + (int)length;
            int p = subtable + 16;
            for (uint i = 0; i < groups; i++, p += 12)
            {
                if (p + 12 > end || p + 12 > data.Length) return 0;
                uint start = ReadUInt32(data, p);
                uint finish = ReadUInt32(data, p + 4);
                if (codePoint < start) return 0;
                if (codePoint <= finish)
                {
                    uint glyph = ReadUInt32(data, p + 8) + codePoint - start;
                    return glyph <= 0xFFFF ? (ushort)glyph : (ushort)0;
                }
            }
            return 0;
        }

        private static Dictionary<ushort, ushort> ReadSymbolMappingsAsUnicodeBmp(byte[] data, TableRecord cmap, CmapRecord record)
        {
            Dictionary<ushort, ushort> result = new Dictionary<ushort, ushort>();
            int subtable = (int)cmap.Offset + (int)record.Offset;
            ushort length = ReadUInt16(data, subtable + 2);
            int end = subtable + length;
            ushort segCount = (ushort)(ReadUInt16(data, subtable + 6) / 2);
            int endCodes = subtable + 14;
            int startCodes = endCodes + segCount * 2 + 2;

            for (int i = 0; i < segCount; i++)
            {
                ushort start = ReadUInt16(data, startCodes + i * 2);
                ushort finish = ReadUInt16(data, endCodes + i * 2);
                if (start == 0xFFFF && finish == 0xFFFF) continue;

                for (uint c = start; c <= finish; c++)
                {
                    ushort unicode;
                    if (c >= 0xF000 && c <= 0xF0FF)
                        unicode = (ushort)(c - 0xF000);
                    else if (c <= 0x00FF)
                        unicode = (ushort)c;
                    else
                        continue;

                    ushort glyph = GetGlyphIdFormat4(data, subtable, (ushort)c);
                    if (glyph != 0 && !result.ContainsKey(unicode)) result.Add(unicode, glyph);
                    if (c == 0xFFFF) break;
                }
            }

            if (subtable + length > end) throw new InvalidDataException("Invalid symbol cmap length.");
            return result;
        }

        private static byte[] BuildFormat4(Dictionary<ushort, ushort> mappings)
        {
            List<KeyValuePair<ushort, ushort>> pairs = mappings
                .Where(p => p.Key != 0xFFFF && p.Value != 0)
                .OrderBy(p => p.Key)
                .ToList();

            int segCount = pairs.Count + 1;
            int length = 16 + segCount * 8;
            byte[] result = new byte[length];

            WriteUInt16(result, 0, 4);
            WriteUInt16(result, 2, (ushort)length);
            WriteUInt16(result, 4, 0);
            WriteUInt16(result, 6, (ushort)(segCount * 2));

            int power = 1;
            int entrySelector = 0;
            while (power * 2 <= segCount)
            {
                power *= 2;
                entrySelector++;
            }
            int searchRange = power * 2;
            WriteUInt16(result, 8, (ushort)searchRange);
            WriteUInt16(result, 10, (ushort)entrySelector);
            WriteUInt16(result, 12, (ushort)(segCount * 2 - searchRange));

            int endCodes = 14;
            int startCodes = endCodes + segCount * 2 + 2;
            int deltas = startCodes + segCount * 2;
            int rangeOffsets = deltas + segCount * 2;

            for (int i = 0; i < pairs.Count; i++)
            {
                ushort code = pairs[i].Key;
                ushort glyph = pairs[i].Value;
                WriteUInt16(result, endCodes + i * 2, code);
                WriteUInt16(result, startCodes + i * 2, code);
                WriteUInt16(result, deltas + i * 2, (ushort)((glyph - code) & 0xFFFF));
                WriteUInt16(result, rangeOffsets + i * 2, 0);
            }

            int sentinel = segCount - 1;
            WriteUInt16(result, endCodes + sentinel * 2, 0xFFFF);
            WriteUInt16(result, endCodes + segCount * 2, 0);
            WriteUInt16(result, startCodes + sentinel * 2, 0xFFFF);
            WriteUInt16(result, deltas + sentinel * 2, 1);
            WriteUInt16(result, rangeOffsets + sentinel * 2, 0);
            return result;
        }

        private static byte[] AddUnicodeBmpRecord(byte[] originalCmap, byte[] unicodeFormat4)
        {
            ushort count = ReadUInt16(originalCmap, 2);
            int oldRecordEnd = 4 + count * 8;
            if (oldRecordEnd > originalCmap.Length) throw new InvalidDataException("Invalid cmap record area.");

            byte[] result = new byte[originalCmap.Length + 8 + unicodeFormat4.Length];
            WriteUInt16(result, 0, ReadUInt16(originalCmap, 0));
            WriteUInt16(result, 2, (ushort)(count + 1));

            for (int i = 0; i < count; i++)
            {
                int oldP = 4 + i * 8;
                int newP = oldP;
                WriteUInt16(result, newP, ReadUInt16(originalCmap, oldP));
                WriteUInt16(result, newP + 2, ReadUInt16(originalCmap, oldP + 2));
                WriteUInt32(result, newP + 4, ReadUInt32(originalCmap, oldP + 4) + 8);
            }

            int addedRecord = 4 + count * 8;
            WriteUInt16(result, addedRecord, 3);
            WriteUInt16(result, addedRecord + 2, 1);
            WriteUInt32(result, addedRecord + 4, (uint)(originalCmap.Length + 8));

            Buffer.BlockCopy(originalCmap, oldRecordEnd, result, oldRecordEnd + 8, originalCmap.Length - oldRecordEnd);
            Buffer.BlockCopy(unicodeFormat4, 0, result, originalCmap.Length + 8, unicodeFormat4.Length);
            return result;
        }

        private static byte[] RebuildSfnt(byte[] original, List<TableRecord> tables, byte[] replacementCmap)
        {
            int tableCount = tables.Count;
            int currentOffset = Align4(12 + tableCount * 16);
            List<byte[]> tableData = new List<byte[]>();
            List<uint> offsets = new List<uint>();
            List<uint> checksums = new List<uint>();
            int headIndex = -1;

            for (int i = 0; i < tableCount; i++)
            {
                TableRecord table = tables[i];
                byte[] bytes = table.Tag == "cmap"
                    ? (byte[])replacementCmap.Clone()
                    : Slice(original, (int)table.Offset, (int)table.Length);

                if (table.Tag == "head" && bytes.Length >= 12)
                {
                    headIndex = i;
                    bytes[8] = bytes[9] = bytes[10] = bytes[11] = 0;
                }

                tableData.Add(bytes);
                offsets.Add((uint)currentOffset);
                checksums.Add(CalculateChecksum(bytes));
                currentOffset += Align4(bytes.Length);
            }

            byte[] output = new byte[currentOffset];
            Buffer.BlockCopy(original, 0, output, 0, Math.Min(12, original.Length));

            for (int i = 0; i < tableCount; i++)
            {
                int directory = 12 + i * 16;
                byte[] tagBytes = Encoding.ASCII.GetBytes(tables[i].Tag);
                Buffer.BlockCopy(tagBytes, 0, output, directory, 4);
                WriteUInt32(output, directory + 4, checksums[i]);
                WriteUInt32(output, directory + 8, offsets[i]);
                WriteUInt32(output, directory + 12, (uint)tableData[i].Length);
                Buffer.BlockCopy(tableData[i], 0, output, (int)offsets[i], tableData[i].Length);
            }

            if (headIndex >= 0)
            {
                uint wholeChecksum = CalculateChecksum(output);
                uint adjustment = unchecked(0xB1B0AFBAu - wholeChecksum);
                WriteUInt32(output, (int)offsets[headIndex] + 8, adjustment);
            }

            return output;
        }

        private static uint CalculateChecksum(byte[] data)
        {
            ulong sum = 0;
            int padded = Align4(data.Length);
            for (int i = 0; i < padded; i += 4)
            {
                uint value = 0;
                for (int j = 0; j < 4; j++)
                {
                    value <<= 8;
                    int index = i + j;
                    if (index < data.Length) value |= data[index];
                }
                sum = (sum + value) & 0xFFFFFFFFu;
            }
            return (uint)sum;
        }

        private static string WindowsEncodingName(ushort encodingId)
        {
            switch (encodingId)
            {
                case 2: return "Windows Shift-JIS";
                case 3: return "Windows PRC";
                case 4: return "Windows Big5";
                case 5: return "Windows Wansung";
                case 6: return "Windows Johab";
                default: return "Windows legacy encoding " + encodingId;
            }
        }

        private static bool RangeInside(byte[] data, uint offset, uint length)
        {
            return offset <= data.Length && length <= data.Length - offset;
        }

        private static byte[] Slice(byte[] data, int offset, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(data, offset, result, 0, length);
            return result;
        }

        private static int Align4(int value)
        {
            return (value + 3) & ~3;
        }

        private static string ReadTag(byte[] data, int offset)
        {
            if (offset < 0 || offset + 4 > data.Length) return string.Empty;
            return Encoding.ASCII.GetString(data, offset, 4);
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            if (offset < 0 || offset + 2 > data.Length) throw new EndOfStreamException();
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static short ReadInt16(byte[] data, int offset)
        {
            return unchecked((short)ReadUInt16(data, offset));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            if (offset < 0 || offset + 4 > data.Length) throw new EndOfStreamException();
            return ((uint)data[offset] << 24) |
                   ((uint)data[offset + 1] << 16) |
                   ((uint)data[offset + 2] << 8) |
                   data[offset + 3];
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
