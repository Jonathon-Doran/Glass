using System;
using System.Buffers;
using Glass.Core;
using LibDeflate;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// SoeDecompressor
//
// Wraps LibDeflate.NET's ZlibDecompressor for SOE protocol packet decompression.
// The SOE protocol uses zlib (RFC 1950) compression on individual packets.
//
// Each SoeDecompressor instance owns a ZlibDecompressor handle and a reusable
// output buffer.  One instance should be created per stream and reused across
// packets to avoid repeated allocation.
//
// All individual compressed packets originate from UDP frames under 512 bytes,
// so the decompressed output is bounded.  The buffer grows if a larger output
// is ever encountered, with a warning logged.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SoeDecompressor : IDisposable
{
    private const int DefaultOutputBufferSize = 16384;

    private ZlibDecompressor? _decompressor;
    private byte[] _outputBuffer;
    private bool _disposed;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SoeDecompressor (constructor)
    //
    // Creates a new decompressor instance with a default output buffer.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SoeDecompressor()
    {
        _decompressor = new ZlibDecompressor();
        _outputBuffer = new byte[DefaultOutputBufferSize];
        _disposed = false;

        DebugLog.Write(DebugLog.Log_Network,
            "SoeDecompressor: created with output buffer size " + DefaultOutputBufferSize);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Decompress
    //
    // Decompresses zlib-compressed packet data into the reusable output buffer.
    // Returns a ReadOnlySpan<byte> over the decompressed data via the out parameter.
    // The span is valid only until the next call to Decompress on this instance.
    //
    // compressedData:  The compressed bytes to decompress
    // decompressed:    Receives a span over the decompressed output on success
    //
    // Returns true on success, false on decompression failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool Decompress(ReadOnlySpan<byte> compressedData,
                           out ReadOnlySpan<byte> decompressed)
    {
        decompressed = ReadOnlySpan<byte>.Empty;

        if (_disposed || _decompressor == null)
        {
            DebugLog.Write("SoeDecompressor.Decompress: not available");
            return false;
        }

        OperationStatus status = _decompressor.Decompress(
            compressedData,
            _outputBuffer.AsSpan(),
            out int bytesWritten);

        if (status == OperationStatus.DestinationTooSmall)
        {
            int newSize = _outputBuffer.Length * 2;
            DebugLog.Write("SoeDecompressor.Decompress: output buffer too small ("
                + _outputBuffer.Length + " bytes), growing to " + newSize);
            _outputBuffer = new byte[newSize];

            status = _decompressor.Decompress(
                compressedData,
                _outputBuffer.AsSpan(),
                out bytesWritten);
        }

        if (status != OperationStatus.Done)
        {
            DebugLog.Write("SoeDecompressor.Decompress: failed, status="
                + status + " compressedLen=" + compressedData.Length);
            return false;
        }

        decompressed = new ReadOnlySpan<byte>(_outputBuffer, 0, bytesWritten);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Releases the underlying ZlibDecompressor handle.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_decompressor != null)
            {
                _decompressor.Dispose();
            }

            _disposed = true;

            DebugLog.Write(DebugLog.Log_Network,
                "SoeDecompressor: disposed");
        }
    }
}