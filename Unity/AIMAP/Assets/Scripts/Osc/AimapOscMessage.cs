using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;

namespace AIMAP.Osc
{
    [Serializable]
    public sealed class AimapOscMessage
    {
        public string Address;
        public List<object> Arguments = new List<object>();
        public IPEndPoint RemoteEndPoint;

        public int ArgumentCount => Arguments?.Count ?? 0;

        public object GetArgument(int index)
        {
            if (Arguments == null || index < 0 || index >= Arguments.Count)
            {
                return null;
            }

            return Arguments[index];
        }

        public int GetInt(int index, int fallback = 0)
        {
            var value = GetArgument(index);
            return AimapOscValueConverter.ToInt(value, fallback);
        }

        public float GetFloat(int index, float fallback = 0f)
        {
            var value = GetArgument(index);
            return AimapOscValueConverter.ToFloat(value, fallback);
        }

        public string GetString(int index, string fallback = "")
        {
            var value = GetArgument(index);
            return AimapOscValueConverter.ToStringValue(value, fallback);
        }

        public bool GetBool(int index, bool fallback = false)
        {
            var value = GetArgument(index);
            return AimapOscValueConverter.ToBool(value, fallback);
        }
    }

    public static class AimapOscValueConverter
    {
        public static int ToInt(object value, int fallback = 0)
        {
            if (value == null)
            {
                return fallback;
            }

            switch (value)
            {
                case int intValue:
                    return intValue;
                case float floatValue:
                    return Mathf.RoundToInt(floatValue);
                case bool boolValue:
                    return boolValue ? 1 : 0;
                case string stringValue when int.TryParse(stringValue, out var parsedInt):
                    return parsedInt;
                case string stringValue when float.TryParse(stringValue, out var parsedFloat):
                    return Mathf.RoundToInt(parsedFloat);
                default:
                    return fallback;
            }
        }

        public static float ToFloat(object value, float fallback = 0f)
        {
            if (value == null)
            {
                return fallback;
            }

            switch (value)
            {
                case float floatValue:
                    return floatValue;
                case int intValue:
                    return intValue;
                case bool boolValue:
                    return boolValue ? 1f : 0f;
                case string stringValue when float.TryParse(stringValue, out var parsedFloat):
                    return parsedFloat;
                case string stringValue when int.TryParse(stringValue, out var parsedInt):
                    return parsedInt;
                default:
                    return fallback;
            }
        }

        public static bool ToBool(object value, bool fallback = false)
        {
            if (value == null)
            {
                return fallback;
            }

            switch (value)
            {
                case bool boolValue:
                    return boolValue;
                case int intValue:
                    return intValue != 0;
                case float floatValue:
                    return !Mathf.Approximately(floatValue, 0f);
                case string stringValue when bool.TryParse(stringValue, out var parsedBool):
                    return parsedBool;
                case string stringValue when int.TryParse(stringValue, out var parsedInt):
                    return parsedInt != 0;
                case string stringValue:
                    return string.Equals(stringValue, "play", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(stringValue, "playing", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(stringValue, "dance", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(stringValue, "dancing", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(stringValue, "on", StringComparison.OrdinalIgnoreCase);
                default:
                    return fallback;
            }
        }

        public static string ToStringValue(object value, string fallback = "")
        {
            return value?.ToString() ?? fallback;
        }
    }

    public static class AimapOscCodec
    {
        private static readonly Encoding OscEncoding = Encoding.UTF8;

        public static bool TryDecode(byte[] data, IPEndPoint remoteEndPoint, out AimapOscMessage message)
        {
            message = null;

            if (data == null || data.Length == 0)
            {
                return false;
            }

            try
            {
                var reader = new OscReader(data);
                var address = reader.ReadPaddedString();

                if (string.IsNullOrWhiteSpace(address))
                {
                    return false;
                }

                if (address == "#bundle")
                {
                    return false;
                }

                var typeTags = reader.ReadPaddedString();
                var decoded = new AimapOscMessage
                {
                    Address = address,
                    RemoteEndPoint = remoteEndPoint,
                };

                if (!string.IsNullOrEmpty(typeTags) && typeTags[0] == ',')
                {
                    for (var index = 1; index < typeTags.Length; index++)
                    {
                        decoded.Arguments.Add(reader.ReadValue(typeTags[index]));
                    }
                }

                message = decoded;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to decode OSC packet: {exception.Message}");
                return false;
            }
        }

        public static byte[] Encode(string address, params object[] arguments)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            WritePaddedString(writer, address);

            var typeTags = new StringBuilder(",");
            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    typeTags.Append(GetTypeTag(argument));
                }
            }

            WritePaddedString(writer, typeTags.ToString());

            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    WriteValue(writer, argument);
                }
            }

            return stream.ToArray();
        }

        private static char GetTypeTag(object value)
        {
            return value switch
            {
                int => 'i',
                bool boolValue => boolValue ? 'T' : 'F',
                string => 's',
                _ => 'f',
            };
        }

        private static void WriteValue(BinaryWriter writer, object value)
        {
            switch (value)
            {
                case int intValue:
                    WriteInt32(writer, intValue);
                    break;
                case bool:
                    break;
                case string stringValue:
                    WritePaddedString(writer, stringValue);
                    break;
                case float floatValue:
                    WriteFloat32(writer, floatValue);
                    break;
                case double doubleValue:
                    WriteFloat32(writer, (float)doubleValue);
                    break;
                default:
                    WriteFloat32(writer, Convert.ToSingle(value));
                    break;
            }
        }

        private static void WritePaddedString(BinaryWriter writer, string value)
        {
            var bytes = OscEncoding.GetBytes(value ?? string.Empty);
            writer.Write(bytes);
            writer.Write((byte)0);

            while (writer.BaseStream.Position % 4 != 0)
            {
                writer.Write((byte)0);
            }
        }

        private static void WriteInt32(BinaryWriter writer, int value)
        {
            writer.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value)));
        }

        private static void WriteFloat32(BinaryWriter writer, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            writer.Write(bytes);
        }

        private sealed class OscReader
        {
            private readonly byte[] _data;
            private int _position;

            public OscReader(byte[] data)
            {
                _data = data;
            }

            public string ReadPaddedString()
            {
                var start = _position;
                while (_position < _data.Length && _data[_position] != 0)
                {
                    _position++;
                }

                var value = OscEncoding.GetString(_data, start, _position - start);

                while (_position < _data.Length && _data[_position] == 0)
                {
                    _position++;
                    if (_position % 4 == 0)
                    {
                        break;
                    }
                }

                while (_position % 4 != 0 && _position < _data.Length)
                {
                    _position++;
                }

                return value;
            }

            public object ReadValue(char typeTag)
            {
                return typeTag switch
                {
                    'i' => ReadInt32(),
                    'f' => ReadFloat32(),
                    's' => ReadPaddedString(),
                    'T' => true,
                    'F' => false,
                    _ => throw new InvalidDataException($"Unsupported OSC type tag: {typeTag}"),
                };
            }

            private int ReadInt32()
            {
                if (_position + 4 > _data.Length)
                {
                    throw new EndOfStreamException();
                }

                var value = (_data[_position] << 24)
                    | (_data[_position + 1] << 16)
                    | (_data[_position + 2] << 8)
                    | _data[_position + 3];

                _position += 4;
                return value;
            }

            private float ReadFloat32()
            {
                if (_position + 4 > _data.Length)
                {
                    throw new EndOfStreamException();
                }

                var bytes = new byte[4];
                Array.Copy(_data, _position, bytes, 0, 4);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                _position += 4;
                return BitConverter.ToSingle(bytes, 0);
            }
        }
    }
}
