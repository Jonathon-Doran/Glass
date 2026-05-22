using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System;
using SharpPcapCapture = SharpPcap.PacketCapture;

namespace Glass.Network.Capture;

///////////////////////////////////////////////////////////////////////////////////////////////
// PcapFileReader
//
// Reads a pcap file and feeds packets through the same PacketRouter
// used by live capture.  Intended for testing and development.
//
// Uses PacketDotNet for header parsing since this is not a real-time path
// and allocation cost is irrelevant.  Processing is synchronous —
// Capture() blocks until all packets are read.
///////////////////////////////////////////////////////////////////////////////////////////////
public class PcapFileReader
{
    private readonly SessionDemux _router;
    private int _frameCount;
    private int _routedCount;
    private int _totalPackets;
    private IProgress<int>? _progress;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PcapFileReader (constructor)
    //
    // router:  The packet router that will receive decoded UDP payloads
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PcapFileReader(SessionDemux router)
    {
        _router = router;
        _frameCount = 0;
        _routedCount = 0;
        _totalPackets = 0;
        _progress = null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessFile
    //
    // Opens a pcap file, reads all packets via Capture(), extracts UDP
    // payload and IP/port metadata via PacketDotNet, and routes through
    // the PacketRouter.  Blocks until all packets are processed.
    //
    // filePath:   Path to the pcap file
    // bpfFilter:  Optional BPF filter string.  Pass null to process all packets.
    //
    // Returns the number of UDP packets successfully routed.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int ProcessFile(string filePath, string? bpfFilter = null, IProgress<int>? progress = null)
    {
        DebugLog.Write("---------");
        DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.ProcessFile: opening '" + filePath + "'");
        DebugLog.Write("---------");

        // Lock out other packet sources until we are done
        if (!GlassContext.TryAcquirePacketSource())
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "PcapFileReader.ProcessFile: packet source claim rejected, aborting load of '" + filePath + "'");
            return 0;
        }
        
        _progress = progress;
        _frameCount = 0;
        _routedCount = 0;
        _totalPackets = CountPackets(filePath, bpfFilter);

        try
        {
            CaptureFileReaderDevice reader = new CaptureFileReaderDevice(filePath);
            reader.Open();

            if (!string.IsNullOrEmpty(bpfFilter))
            {
                reader.Filter = bpfFilter;
                DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.ProcessFile: filter='" + bpfFilter + "'");
            }

            reader.OnPacketArrival += OnPacketArrival;

            reader.Capture();
            reader.Close();
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.ProcessFile: error reading '"
                + filePath + "': " + ex.Message);
            DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.ProcessFile: stack trace: "
                 + ex.StackTrace);
        }
        finally
        {
            GlassContext.ReleasePacketSource();
            _progress = null;
        }

        DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.ProcessFile: finished, "
                    + _frameCount + " packets read of " + _totalPackets + " total, "
                    + _routedCount + " UDP packets routed");

        return _routedCount;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CountPackets
    //
    // Opens the pcap file, scans it without decoding, and returns the number of packets that
    // match the optional BPF filter.  Used as a pre-scan pass by ProcessFile so a progress bar
    // can show percentage rather than an open-ended packet counter.
    //
    // The caller must already hold the packet source claim.  This method opens its own
    // CaptureFileReaderDevice, separate from ProcessFile's, and closes it before returning.
    //
    // On error, the count up to the failure point is returned and the error is logged.  The
    // caller can decide whether to abort or proceed with an approximate count.
    //
    // filePath:   Path to the pcap file.
    // bpfFilter:  Optional BPF filter string.  Pass null to count all packets.
    //
    // Returns:    The number of packets found in the file, possibly partial if an error occurred.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private int CountPackets(string filePath, string? bpfFilter)
    {
        int count = 0;

        DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.CountPackets: scanning '" + filePath + "'");

        try
        {
            CaptureFileReaderDevice reader = new CaptureFileReaderDevice(filePath);
            reader.Open();

            if (!string.IsNullOrEmpty(bpfFilter))
            {
                reader.Filter = bpfFilter;
            }

            while (true)
            {
                SharpPcapCapture packet;
                GetPacketStatus status = reader.GetNextPacket(out packet);

                if (status != GetPacketStatus.PacketRead)
                {
                    break;
                }

                count++;
            }

            reader.Close();
        }
        catch (Exception ex)
        {
            DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.CountPackets: error scanning '"
                + filePath + "' at count " + count + ": " + ex.Message);
        }

        DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.CountPackets: counted "
            + count + " packets in '" + filePath + "'");

        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OnPacketArrival
    //
    // Called by SharpPcap for each packet in the pcap file.  Uses PacketDotNet
    // to parse headers and extract the UDP payload.
    //
    // sender:  The capture file reader device
    // e:       The captured packet data
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void OnPacketArrival(object sender, SharpPcapCapture e)
    {
        _frameCount++;

        if (_progress != null && (_frameCount % 1000 == 0) && _totalPackets > 0)
        {
            int percent = (int)((long)_frameCount * 100 / _totalPackets);
            _progress.Report(percent);
        }

        RawCapture rawCapture = e.GetPacket();

        if (rawCapture == null)
        {
            return;
        }

        DebugLog.BeginTimestampGroup(rawCapture.Timeval.Date.ToLocalTime());

        Packet packet = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);

        if (packet == null)
        {
            return;
        }

        IPv4Packet ipPacket = packet.Extract<IPv4Packet>();

        if (ipPacket == null)
        {
            return;
        }

        UdpPacket udpPacket = ipPacket.Extract<UdpPacket>();

        if (udpPacket == null)
        {
            return;
        }

        byte[] payloadBytes = udpPacket.PayloadData;

        if (payloadBytes == null || payloadBytes.Length == 0)
        {
            return;
        }

        UdpDatagram dgram = new UdpDatagram();
        dgram.FrameNumber = _frameCount;
        dgram.Timestamp = rawCapture.Timeval.Date;
        dgram.SourceIp = ipPacket.SourceAddress.ToString();
        dgram.SourcePort = udpPacket.SourcePort;
        dgram.DestIp = ipPacket.DestinationAddress.ToString();
        dgram.DestPort = udpPacket.DestinationPort;
        dgram.Payload = GlassContext.BufferPool.Rent((uint) payloadBytes.Length);

        payloadBytes.AsSpan(0, payloadBytes.Length).CopyTo(dgram.Payload.AsSpan());

        _router.RoutePacket(dgram);

        _routedCount++;
    }
}