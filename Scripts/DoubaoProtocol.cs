using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

public static class DoubaoProtocol
{
    public enum MessageType
    {
        FullClientRequest = 0x1,
        AudioOnlyRequest = 0x2,
        FullServerResponse = 0x9,
        ErrorResponse = 0xF
    }

    public enum SerializationMethod
    {
        None = 0x0,
        Json = 0x1
    }

    public enum CompressionMethod
    {
        None = 0x0,
        Gzip = 0x1
    }

    public const byte ProtocolVersion = 0x1;
    public const byte HeaderSize = 0x1;

    public static byte[] BuildFullClientRequest(string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] compressed = GzipCompress(payload);

        byte[] header = BuildHeader(
            MessageType.FullClientRequest,
            0x0,
            SerializationMethod.Json,
            CompressionMethod.Gzip);

        return Combine(
            header,
            IntToBigEndianBytes(compressed.Length),
            compressed);
    }

    public static byte[] BuildAudioOnlyRequest(byte[] audioData, bool isLastPacket)
    {
        int dataSize = audioData != null ? audioData.Length : 0;

        var header = new byte[4];

        // 修复点：header_size 不能是 0，必须是 HeaderSize(=1，即4字节头)
        header[0] = (byte)((ProtocolVersion << 4) | HeaderSize);

        header[1] = (byte)(((int)MessageType.AudioOnlyRequest << 4) + 0);

        // 音频包不是 JSON
        header[2] = (byte)(((int)SerializationMethod.None << 4) | (int)CompressionMethod.None);

        // 最后一包标记
        header[3] = isLastPacket ? (byte)0x80 : (byte)0x00;

        // 始终带 payloadSize（最后一包可为 0）
        return Combine(
            header,
            IntToBigEndianBytes(dataSize),
            dataSize > 0 ? audioData : Array.Empty<byte>());
    }

    public static DoubaoParsedMessage ParseServerMessage(byte[] data)
    {
        if (data == null || data.Length < 8)
        {
            Debug.LogError("DoubaoProtocol: 服务端消息长度不足");
            return null;
        }

        byte b0 = data[0];
        byte b1 = data[1];
        byte b2 = data[2];

        int protocolVersion = (b0 >> 4) & 0x0F;
        int headerSizeUnits = b0 & 0x0F;
        int headerSizeBytes = headerSizeUnits * 4;

        int messageType = (b1 >> 4) & 0x0F;
        int flags = b1 & 0x0F;

        int serialization = (b2 >> 4) & 0x0F;
        int compression = b2 & 0x0F;

        if (data.Length < headerSizeBytes)
        {
            Debug.LogError("DoubaoProtocol: headerSize 非法");
            return null;
        }

        if (messageType == (int)MessageType.FullServerResponse)
        {
            // 兼容两种格式：
            // A) payloadSize + payload
            // B) sequence + payloadSize + payload

            int sequence = 0;
            int payloadSize = -1;
            int payloadOffset = -1;

            // 尝试 A
            if (data.Length >= headerSizeBytes + 4)
            {
                int aPayloadSize = BigEndianBytesToInt(data, headerSizeBytes);
                int aPayloadOffset = headerSizeBytes + 4;
                bool aValid = aPayloadSize >= 0 && aPayloadOffset + aPayloadSize <= data.Length;

                if (aValid)
                {
                    payloadSize = aPayloadSize;
                    payloadOffset = aPayloadOffset;
                }
            }

            // 尝试 B（优先在 A 无效时使用）
            if ((payloadOffset < 0) && data.Length >= headerSizeBytes + 8)
            {
                int bSequence = BigEndianBytesToInt(data, headerSizeBytes);
                int bPayloadSize = BigEndianBytesToInt(data, headerSizeBytes + 4);
                int bPayloadOffset = headerSizeBytes + 8;
                bool bValid = bPayloadSize >= 0 && bPayloadOffset + bPayloadSize <= data.Length;

                if (bValid)
                {
                    sequence = bSequence;
                    payloadSize = bPayloadSize;
                    payloadOffset = bPayloadOffset;
                }
            }

            if (payloadOffset < 0)
            {
                Debug.LogError($"DoubaoProtocol: FullServerResponse 解析失败，dataLength={data.Length}, headerSizeBytes={headerSizeBytes}");
                return null;
            }

            byte[] payload = new byte[payloadSize];
            if (payloadSize > 0)
            {
                Buffer.BlockCopy(data, payloadOffset, payload, 0, payloadSize);
            }

            if (compression == (int)CompressionMethod.Gzip)
            {
                payload = GzipDecompress(payload);
                if (payload == null)
                {
                    return null;
                }
            }

            string json = serialization == (int)SerializationMethod.Json
                ? Encoding.UTF8.GetString(payload)
                : string.Empty;

            return new DoubaoParsedMessage
            {
                ProtocolVersion = protocolVersion,
                HeaderSizeBytes = headerSizeBytes,
                MessageType = messageType,
                Flags = flags,
                Sequence = sequence,
                PayloadJson = json
            };
        }
        else if (messageType == (int)MessageType.ErrorResponse)
        {
            if (data.Length < headerSizeBytes + 8)
            {
                Debug.LogError("DoubaoProtocol: ErrorResponse 长度不足");
                return null;
            }

            int errorCode = BigEndianBytesToInt(data, headerSizeBytes);
            int payloadSize = BigEndianBytesToInt(data, headerSizeBytes + 4);

            if (payloadSize < 0 || data.Length < headerSizeBytes + 8 + payloadSize)
            {
                Debug.LogError("DoubaoProtocol: Error payloadSize 非法");
                return null;
            }

            byte[] payload = new byte[payloadSize];
            Buffer.BlockCopy(data, headerSizeBytes + 8, payload, 0, payloadSize);

            string errorMessage = Encoding.UTF8.GetString(payload);

            return new DoubaoParsedMessage
            {
                ProtocolVersion = protocolVersion,
                HeaderSizeBytes = headerSizeBytes,
                MessageType = messageType,
                Flags = flags,
                ErrorCode = errorCode,
                PayloadJson = errorMessage
            };
        }

        return new DoubaoParsedMessage
        {
            ProtocolVersion = protocolVersion,
            HeaderSizeBytes = headerSizeBytes,
            MessageType = messageType,
            Flags = flags
        };
    }

    private static byte[] BuildHeader(
        MessageType messageType,
        byte messageTypeSpecificFlags,
        SerializationMethod serializationMethod,
        CompressionMethod compressionMethod)
    {
        byte[] header = new byte[4];

        header[0] = (byte)((ProtocolVersion << 4) | HeaderSize);
        header[1] = (byte)(((byte)messageType << 4) | (messageTypeSpecificFlags & 0x0F));
        header[2] = (byte)(((byte)serializationMethod << 4) | ((byte)compressionMethod & 0x0F));
        header[3] = 0x00;

        return header;
    }

    public static byte[] IntToBigEndianBytes(int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return bytes;
    }

    public static int BigEndianBytesToInt(byte[] bytes, int offset)
    {
        byte[] temp = new byte[4];
        Buffer.BlockCopy(bytes, offset, temp, 0, 4);

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(temp);
        }

        return BitConverter.ToInt32(temp, 0);
    }

    public static byte[] GzipCompress(byte[] input)
    {
        try
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress))
                {
                    gzip.Write(input, 0, input.Length);
                }

                return output.ToArray();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Gzip 压缩失败: {e.Message}");
            return null;
        }
    }

    public static byte[] GzipDecompress(byte[] input)
    {
        try
        {
            using (MemoryStream inputStream = new MemoryStream(input))
            using (GZipStream gzip = new GZipStream(inputStream, CompressionMode.Decompress))
            using (MemoryStream outputStream = new MemoryStream())
            {
                gzip.CopyTo(outputStream);
                return outputStream.ToArray();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Gzip 解压失败: {e.Message}");
            return null;
        }
    }

    private static byte[] Combine(params byte[][] arrays)
    {
        int totalLength = 0;
        foreach (var arr in arrays)
        {
            if (arr != null)
            {
                totalLength += arr.Length;
            }
        }

        byte[] result = new byte[totalLength];
        int offset = 0;

        foreach (var arr in arrays)
        {
            if (arr == null)
            {
                continue;
            }

            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }

        return result;
    }
}

public class DoubaoParsedMessage
{
    public int ProtocolVersion;
    public int HeaderSizeBytes;
    public int MessageType;
    public int Flags;
    public int Sequence;
    public int ErrorCode;
    public string PayloadJson;
}