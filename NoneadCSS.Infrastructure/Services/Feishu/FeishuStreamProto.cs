using System.Text;

namespace NoneadCSS.Infrastructure.Services.Feishu;

/// <summary>
/// 飞书长连接 Protobuf Frame 帧类型与消息类型常量。
/// 参考飞书 Go/Node.js SDK 源码中的枚举定义。
/// </summary>
internal static class FeishuStreamConstants
{
    /// <summary>帧类型：控制帧（Ping/Pong）</summary>
    public const int FrameTypeControl = 0;

    /// <summary>帧类型：数据帧（事件传输）</summary>
    public const int FrameTypeData = 1;

    /// <summary>Headers 中 type 字段的 key 名</summary>
    public const string HeaderKeyType = "type";

    /// <summary>消息类型：心跳 Ping</summary>
    public const string MessageTypePing = "ping";

    /// <summary>消息类型：心跳 Pong 响应</summary>
    public const string MessageTypePong = "pong";

    /// <summary>消息类型：数据事件</summary>
    public const string MessageDataType = "data";

    /// <summary>消息类型：ACK 确认</summary>
    public const string MessageTypeAck = "ack";

    /// <summary>消息类型：关闭通知</summary>
    public const string MessageTypeClose = "close";
}

/// <summary>
/// 飞书长连接 Protobuf Header 结构（key-value 对）。
/// protobuf: message Header { string key = 1; string value = 2; }
/// </summary>
internal sealed class FeishuStreamHeader
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public FeishuStreamHeader() { }

    public FeishuStreamHeader(string key, string value)
    {
        Key = key;
        Value = value;
    }
}

/// <summary>
/// 飞书长连接 Protobuf Frame 结构。
/// protobuf:
///   message Frame {
///     uint64 SeqID = 1;
///     uint64 LogID = 2;
///     int32  service = 3;
///     int32  method = 4;           // 0=控制帧 1=数据帧
///     repeated Header headers = 5;
///     string payload_encoding = 6;
///     string payload_type = 7;
///     bytes  payload = 8;
///   }
/// 手动实现 Protobuf 编解码，无需引入 Google.Protobuf 依赖。
/// </summary>
internal sealed class FeishuStreamFrame
{
    public ulong SeqId { get; set; }
    public ulong LogId { get; set; }
    public int Service { get; set; }
    public int Method { get; set; }
    public List<FeishuStreamHeader> Headers { get; set; } = new();
    public string PayloadEncoding { get; set; } = string.Empty;
    public string PayloadType { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>获取 Headers 中指定 key 的值</summary>
    public string? GetHeader(string key)
    {
        foreach (var h in Headers)
        {
            if (string.Equals(h.Key, key, StringComparison.Ordinal))
                return h.Value;
        }
        return null;
    }

    /// <summary>设置 Headers 中指定 key 的值（存在则更新，不存在则添加）</summary>
    public void SetHeader(string key, string value)
    {
        for (var i = 0; i < Headers.Count; i++)
        {
            if (string.Equals(Headers[i].Key, key, StringComparison.Ordinal))
            {
                Headers[i].Value = value;
                return;
            }
        }
        Headers.Add(new FeishuStreamHeader(key, value));
    }

    // ===== Protobuf wire format 常量 =====
    // Tag = (field_number << 3) | wire_type
    // wire_type 0 = Varint, wire_type 2 = Length-delimited
    private const int WireTypeVarint = 0;
    private const int WireTypeLengthDelimited = 2;

    private const int TagSeqId = (1 << 3) | WireTypeVarint;            // 0x08
    private const int TagLogId = (2 << 3) | WireTypeVarint;            // 0x10
    private const int TagService = (3 << 3) | WireTypeVarint;          // 0x18
    private const int TagMethod = (4 << 3) | WireTypeVarint;           // 0x20
    private const int TagHeaders = (5 << 3) | WireTypeLengthDelimited; // 0x2A
    private const int TagPayloadEncoding = (6 << 3) | WireTypeLengthDelimited; // 0x32
    private const int TagPayloadType = (7 << 3) | WireTypeLengthDelimited;     // 0x3A
    private const int TagPayload = (8 << 3) | WireTypeLengthDelimited;         // 0x42

    // Header 子消息字段 tag
    private const int TagHeaderKey = (1 << 3) | WireTypeLengthDelimited;   // 0x0A
    private const int TagHeaderValue = (2 << 3) | WireTypeLengthDelimited; // 0x12

    /// <summary>
    /// 将 Frame 序列化为 Protobuf 二进制。
    /// </summary>
    public byte[] ToByteArray()
    {
        using var ms = new MemoryStream(256);
        // SeqID (field 1, varint)
        if (SeqId != 0)
        {
            WriteTag(ms, TagSeqId);
            WriteVarint(ms, SeqId);
        }
        // LogID (field 2, varint)
        if (LogId != 0)
        {
            WriteTag(ms, TagLogId);
            WriteVarint(ms, LogId);
        }
        // Service (field 3, varint)
        if (Service != 0)
        {
            WriteTag(ms, TagService);
            WriteVarint(ms, (ulong)Service);
        }
        // Method (field 4, varint)
        if (Method != 0)
        {
            WriteTag(ms, TagMethod);
            WriteVarint(ms, (ulong)Method);
        }
        // Headers (field 5, repeated embedded message)
        foreach (var header in Headers)
        {
            var headerBytes = SerializeHeader(header);
            WriteTag(ms, TagHeaders);
            WriteVarint(ms, (ulong)headerBytes.Length);
            ms.Write(headerBytes, 0, headerBytes.Length);
        }
        // payload_encoding (field 6, string)
        if (!string.IsNullOrEmpty(PayloadEncoding))
        {
            var bytes = Encoding.UTF8.GetBytes(PayloadEncoding);
            WriteTag(ms, TagPayloadEncoding);
            WriteVarint(ms, (ulong)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        // payload_type (field 7, string)
        if (!string.IsNullOrEmpty(PayloadType))
        {
            var bytes = Encoding.UTF8.GetBytes(PayloadType);
            WriteTag(ms, TagPayloadType);
            WriteVarint(ms, (ulong)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        // payload (field 8, bytes)
        if (Payload != null && Payload.Length > 0)
        {
            WriteTag(ms, TagPayload);
            WriteVarint(ms, (ulong)Payload.Length);
            ms.Write(Payload, 0, Payload.Length);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// 从 Protobuf 二进制反序列化 Frame。
    /// </summary>
    public static FeishuStreamFrame Parse(ReadOnlySpan<byte> data)
    {
        var frame = new FeishuStreamFrame();
        var pos = 0;
        while (pos < data.Length)
        {
            var tag = ReadVarint(data, ref pos);
            var fieldNumber = (int)(tag >> 3);
            var wireType = (int)(tag & 0x07);

            switch (fieldNumber)
            {
                case 1: // SeqID (varint)
                    frame.SeqId = ReadVarint(data, ref pos);
                    break;
                case 2: // LogID (varint)
                    frame.LogId = ReadVarint(data, ref pos);
                    break;
                case 3: // Service (varint)
                    frame.Service = (int)ReadVarint(data, ref pos);
                    break;
                case 4: // Method (varint)
                    frame.Method = (int)ReadVarint(data, ref pos);
                    break;
                case 5: // Headers (length-delimited embedded message)
                    {
                        var len = (int)ReadVarint(data, ref pos);
                        var headerData = data.Slice(pos, len);
                        frame.Headers.Add(ParseHeader(headerData));
                        pos += len;
                        break;
                    }
                case 6: // payload_encoding (string)
                    {
                        var len = (int)ReadVarint(data, ref pos);
                        frame.PayloadEncoding = Encoding.UTF8.GetString(data.Slice(pos, len));
                        pos += len;
                        break;
                    }
                case 7: // payload_type (string)
                    {
                        var len = (int)ReadVarint(data, ref pos);
                        frame.PayloadType = Encoding.UTF8.GetString(data.Slice(pos, len));
                        pos += len;
                        break;
                    }
                case 8: // payload (bytes)
                    {
                        var len = (int)ReadVarint(data, ref pos);
                        frame.Payload = data.Slice(pos, len).ToArray();
                        pos += len;
                        break;
                    }
                default:
                    // 未知字段：按 wire type 跳过
                    SkipField(data, ref pos, wireType);
                    break;
            }
        }
        return frame;
    }

    private byte[] SerializeHeader(FeishuStreamHeader header)
    {
        using var ms = new MemoryStream(64);
        if (!string.IsNullOrEmpty(header.Key))
        {
            var bytes = Encoding.UTF8.GetBytes(header.Key);
            WriteTag(ms, TagHeaderKey);
            WriteVarint(ms, (ulong)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        if (!string.IsNullOrEmpty(header.Value))
        {
            var bytes = Encoding.UTF8.GetBytes(header.Value);
            WriteTag(ms, TagHeaderValue);
            WriteVarint(ms, (ulong)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        return ms.ToArray();
    }

    private static FeishuStreamHeader ParseHeader(ReadOnlySpan<byte> data)
    {
        var header = new FeishuStreamHeader();
        var pos = 0;
        while (pos < data.Length)
        {
            var tag = ReadVarint(data, ref pos);
            var fieldNumber = (int)(tag >> 3);
            var wireType = (int)(tag & 0x07);
            var len = (int)ReadVarint(data, ref pos);
            switch (fieldNumber)
            {
                case 1: // key
                    header.Key = Encoding.UTF8.GetString(data.Slice(pos, len));
                    break;
                case 2: // value
                    header.Value = Encoding.UTF8.GetString(data.Slice(pos, len));
                    break;
            }
            pos += len;
        }
        return header;
    }

    // ===== Protobuf 基础读写工具 =====

    private static void WriteTag(MemoryStream ms, int tag) => WriteVarint(ms, (ulong)tag);

    private static void WriteVarint(MemoryStream ms, ulong value)
    {
        while (value > 0x7F)
        {
            ms.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        ms.WriteByte((byte)value);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int pos)
    {
        ulong result = 0;
        var shift = 0;
        while (pos < data.Length)
        {
            var b = data[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift >= 64)
                throw new InvalidDataException("Protobuf varint 太长，超出 uint64 范围");
        }
        return result;
    }

    private static void SkipField(ReadOnlySpan<byte> data, ref int pos, int wireType)
    {
        switch (wireType)
        {
            case 0: // varint
                ReadVarint(data, ref pos);
                break;
            case 1: // 64-bit
                pos += 8;
                break;
            case 2: // length-delimited
                var len = (int)ReadVarint(data, ref pos);
                pos += len;
                break;
            case 5: // 32-bit
                pos += 4;
                break;
            default:
                throw new InvalidDataException($"未知的 Protobuf wire type: {wireType}");
        }
    }

    // ===== 工厂方法 =====

    /// <summary>创建 Ping 控制帧（用于心跳）</summary>
    public static FeishuStreamFrame CreatePingFrame(int serviceId)
    {
        return new FeishuStreamFrame
        {
            SeqId = 0,
            LogId = 0,
            Service = serviceId,
            Method = FeishuStreamConstants.FrameTypeControl,
            Headers = new List<FeishuStreamHeader>
            {
                new(FeishuStreamConstants.HeaderKeyType, FeishuStreamConstants.MessageTypePing)
            }
        };
    }

    /// <summary>创建 Pong 控制帧（响应 Ping）</summary>
    public static FeishuStreamFrame CreatePongFrame(int serviceId)
    {
        return new FeishuStreamFrame
        {
            SeqId = 0,
            LogId = 0,
            Service = serviceId,
            Method = FeishuStreamConstants.FrameTypeControl,
            Headers = new List<FeishuStreamHeader>
            {
                new(FeishuStreamConstants.HeaderKeyType, FeishuStreamConstants.MessageTypePong)
            }
        };
    }

    /// <summary>创建 ACK 数据帧（确认收到事件）</summary>
    public static FeishuStreamFrame CreateAckFrame(ulong seqId, ulong logId, int serviceId, byte[] payload)
    {
        return new FeishuStreamFrame
        {
            SeqId = seqId,
            LogId = logId,
            Service = serviceId,
            Method = FeishuStreamConstants.FrameTypeData,
            Headers = new List<FeishuStreamHeader>
            {
                new(FeishuStreamConstants.HeaderKeyType, FeishuStreamConstants.MessageTypeAck)
            },
            Payload = payload
        };
    }
}
