# Nexus.Archive

This directory contains a source-maintained copy of
[Nexus.Archive](https://github.com/jasoncouture/Nexus.Archive), a WildStar index
and archive reader written by Jason Couture.

The copy was based on upstream commit
`28cbb96465bc6b0b79353de2bae3feeb8d10c108` (24 April 2025). It is distributed
under GPL-3.0; the complete licence is retained in `LICENSE.GPL-3.0.txt`.

NexusForever modifications made on 10 August 2026:

- target .NET 10 and use the repository's centrally managed dependencies;
- update the LZMA decoder call for SharpCompress 0.50.4;
- validate compressed stream sizes and truncated LZMA properties;
- expose internals to the compatibility test project.

The source copy exists because Nexus.Archive 1.0.1 depends on SharpCompress
0.22.0. A binary dependency override is unsafe because the modern
SharpCompress LZMA API is not binary-compatible with that package.
