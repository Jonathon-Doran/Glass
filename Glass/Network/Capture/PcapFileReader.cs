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
    public int ProcessFile(string filePath, string? bpfFilter = null)
    {
        DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.ProcessFile: opening '" + filePath + "'");

        _frameCount = 0;
        _routedCount = 0;

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
            //DebugLog.SuppressUI = true;
            reader.Capture();
            reader.Close();
            //DebugLog.SuppressUI = false;
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
            //DebugLog.SuppressUI = false;
        }

        DebugLog.Write(LogChannel.LowNetwork, "PcapFileReader.ProcessFile: finished, "
            + _frameCount + " packets read, "
            + _routedCount + " UDP packets routed");

        return _routedCount;
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

        RawCapture rawCapture = e.GetPacket();

        if (rawCapture == null)
        {
            return;
        }

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

        PacketMetadata metadata = new PacketMetadata();
        metadata.FrameNumber = _frameCount;
        metadata.Timestamp = rawCapture.Timeval.Date;
        metadata.SourceIp = ipPacket.SourceAddress.ToString();
        metadata.SourcePort = udpPacket.SourcePort;
        metadata.DestIp = ipPacket.DestinationAddress.ToString();
        metadata.DestPort = udpPacket.DestinationPort;

        ReadOnlySpan<byte> payload = new ReadOnlySpan<byte>(payloadBytes);

        _router.RoutePacket(payload, metadata);

        _routedCount++;
    }
}