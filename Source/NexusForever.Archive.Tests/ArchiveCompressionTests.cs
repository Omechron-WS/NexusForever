using System.IO.Compression;
using System.Text;
using Nexus.Archive;
using ArchiveReader = Nexus.Archive.Archive;

namespace NexusForever.Archive.Tests
{
    public sealed class ArchiveCompressionTests
    {
        private const string ExpectedContent = "NexusForever build 16042 archive compatibility.";

        private static readonly byte[] lzmaData =
        [
            0x5d, 0x00, 0x00, 0x80, 0x00,
            0x00, 0x27, 0x19, 0x4b, 0x07, 0x7a, 0x54, 0x25, 0xc9,
            0x65, 0xb8, 0xf9, 0x0b, 0x9a, 0x3d, 0x98, 0x0b, 0xc7, 0x4c,
            0xe5, 0x42, 0x10, 0x9f, 0x45, 0x1c, 0x78, 0x85, 0x29, 0x55,
            0x44, 0xb3, 0x1d, 0x3e, 0x95, 0x98, 0x0c, 0x86, 0x9f, 0x90,
            0x79, 0x45, 0x80, 0x19, 0x7d, 0xed, 0xef, 0x78, 0x9d, 0x4c,
            0x08, 0xde, 0xff, 0xfe, 0x2a, 0x18, 0x00
        ];

        [Fact]
        public void HandleCompression_LzmaFixtureDecodesWithModernSharpCompress()
        {
            using var input = new MemoryStream(lzmaData);
            using Stream output = ArchiveReader.HandleCompression(
                ArchiveFileFlags.CompressedLzma,
                lzmaData.Length,
                Encoding.UTF8.GetByteCount(ExpectedContent),
                input);
            using var reader = new StreamReader(output, Encoding.UTF8);

            Assert.Equal(ExpectedContent, reader.ReadToEnd());
        }

        [Fact]
        public void HandleCompression_DeflateFixtureDecodes()
        {
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var compressor = new DeflateStream(output, CompressionMode.Compress, true))
                    compressor.Write(Encoding.UTF8.GetBytes(ExpectedContent));

                compressed = output.ToArray();
            }

            using var input = new MemoryStream(compressed);
            using Stream outputStream = ArchiveReader.HandleCompression(
                ArchiveFileFlags.CompressedDeflate,
                compressed.Length,
                Encoding.UTF8.GetByteCount(ExpectedContent),
                input);
            using var reader = new StreamReader(outputStream, Encoding.UTF8);

            Assert.Equal(ExpectedContent, reader.ReadToEnd());
        }

        [Theory]
        [InlineData(-1L)]
        [InlineData(0L)]
        [InlineData(4L)]
        public void HandleCompression_InvalidLzmaCompressedSizeFailsClosed(long compressedSize)
        {
            Assert.Throws<InvalidDataException>(() =>
                ArchiveReader.HandleCompression(
                    ArchiveFileFlags.CompressedLzma,
                    compressedSize,
                    Encoding.UTF8.GetByteCount(ExpectedContent),
                    new MemoryStream(lzmaData)));
        }

        [Fact]
        public void HandleCompression_TruncatedLzmaPropertiesFailsClosed()
        {
            Assert.Throws<EndOfStreamException>(() =>
                ArchiveReader.HandleCompression(
                    ArchiveFileFlags.CompressedLzma,
                    lzmaData.Length,
                    Encoding.UTF8.GetByteCount(ExpectedContent),
                    new MemoryStream([0x5d, 0x00, 0x00, 0x80])));
        }

        [Fact]
        public void HandleCompression_MultipleCompressionFormatsFailClosed()
        {
            using var input = new MemoryStream(lzmaData);

            Assert.Throws<InvalidDataException>(() =>
                ArchiveReader.HandleCompression(
                    ArchiveFileFlags.CompressedLzma | ArchiveFileFlags.CompressedDeflate,
                    lzmaData.Length,
                    Encoding.UTF8.GetByteCount(ExpectedContent),
                    input));
        }
    }
}
