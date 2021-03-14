﻿using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Globalization;

using Macross.Json.Extensions;

namespace System.Text.Json.Serialization
{
	internal class JsonStringEnumMemberConverterHelper<TEnum>
		where TEnum : struct, Enum
	{
		private class EnumInfo
		{
#pragma warning disable SA1401 // Fields should be private
			public string Name;
			public TEnum EnumValue;
			public ulong RawValue;
#pragma warning restore SA1401 // Fields should be private

			public EnumInfo(string name, TEnum enumValue, ulong rawValue)
			{
				Name = name;
				EnumValue = enumValue;
				RawValue = rawValue;
			}
		}

		private const BindingFlags EnumBindings = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

#if NETSTANDARD2_0
		private static readonly string[] s_Split = new string[] { ", " };
#endif

		private readonly bool _AllowIntegerValues;
		private readonly TEnum? _DeserializationFailureFallbackValue;
		private readonly Type _EnumType;
		private readonly TypeCode _EnumTypeCode;
		private readonly bool _IsFlags;
		private readonly Dictionary<TEnum, EnumInfo> _RawToTransformed;
		private readonly Dictionary<string, EnumInfo> _TransformedToRaw;

		public JsonStringEnumMemberConverterHelper(JsonNamingPolicy? namingPolicy, bool allowIntegerValues, ulong? deserializationFailureFallbackValue)
		{
			_AllowIntegerValues = allowIntegerValues;
			_EnumType = typeof(TEnum);
			_EnumTypeCode = Type.GetTypeCode(_EnumType);
			_IsFlags = _EnumType.IsDefined(typeof(FlagsAttribute), true);

			string[] builtInNames = _EnumType.GetEnumNames();
			Array builtInValues = _EnumType.GetEnumValues();

			_RawToTransformed = new Dictionary<TEnum, EnumInfo>();
			_TransformedToRaw = new Dictionary<string, EnumInfo>();

			for (int i = 0; i < builtInNames.Length; i++)
			{
				Enum? enumValue = (Enum?)builtInValues.GetValue(i);
				if (enumValue == null)
					continue;
				ulong rawValue = GetEnumValue(enumValue);

				string name = builtInNames[i];
				FieldInfo field = _EnumType.GetField(name, EnumBindings)!;

				string transformedName = field.GetCustomAttribute<EnumMemberAttribute>(true)?.Value ??
										 field.GetCustomAttribute<JsonPropertyNameAttribute>(true)?.Name ??
										 namingPolicy?.ConvertName(name) ??
										 name;

				if (enumValue is not TEnum typedValue)
					throw new NotSupportedException();

				if (deserializationFailureFallbackValue.HasValue && rawValue == deserializationFailureFallbackValue)
					_DeserializationFailureFallbackValue = typedValue;

				_RawToTransformed[typedValue] = new EnumInfo(transformedName, typedValue, rawValue);
				_TransformedToRaw[transformedName] = new EnumInfo(name, typedValue, rawValue);
			}
		}

		public TEnum Read(ref Utf8JsonReader reader)
		{
			JsonTokenType token = reader.TokenType;

			if (token == JsonTokenType.String)
			{
				string enumString = reader.GetString()!;

				// Case sensitive search attempted first.
				if (_TransformedToRaw.TryGetValue(enumString, out EnumInfo? enumInfo))
					return enumInfo.EnumValue;

				if (_IsFlags)
				{
					ulong calculatedValue = 0;

#if NETSTANDARD2_0
					string[] flagValues = enumString.Split(s_Split, StringSplitOptions.None);
#else
					string[] flagValues = enumString.Split(", ");
#endif
					foreach (string flagValue in flagValues)
					{
						// Case sensitive search attempted first.
						if (_TransformedToRaw.TryGetValue(flagValue, out enumInfo))
						{
							calculatedValue |= enumInfo.RawValue;
						}
						else
						{
							// Case insensitive search attempted second.
							bool matched = false;
							foreach (KeyValuePair<string, EnumInfo> enumItem in _TransformedToRaw)
							{
								if (string.Equals(enumItem.Key, flagValue, StringComparison.OrdinalIgnoreCase))
								{
									calculatedValue |= enumItem.Value.RawValue;
									matched = true;
									break;
								}
							}

							if (!matched)
								return ReturnDefaultValueOrThrowJsonException(flagValue);
						}
					}

					TEnum enumValue = (TEnum)Enum.ToObject(_EnumType, calculatedValue);
					if (_TransformedToRaw.Count < 64)
					{
						_TransformedToRaw[enumString] = new EnumInfo(enumString, enumValue, calculatedValue);
					}
					return enumValue;
				}

				// Case insensitive search attempted second.
				foreach (KeyValuePair<string, EnumInfo> enumItem in _TransformedToRaw)
				{
					if (string.Equals(enumItem.Key, enumString, StringComparison.OrdinalIgnoreCase))
					{
						return enumItem.Value.EnumValue;
					}
				}

				return ReturnDefaultValueOrThrowJsonException(enumString);
			}

			if (token != JsonTokenType.Number || !_AllowIntegerValues)
			{
				if (_DeserializationFailureFallbackValue.HasValue)
				{
					reader.Skip();
					return _DeserializationFailureFallbackValue.Value;
				}
				throw ThrowHelper.GenerateJsonException_DeserializeUnableToConvertValue(_EnumType);
			}

			switch (_EnumTypeCode)
			{
				case TypeCode.Int32:
					if (reader.TryGetInt32(out int int32))
					{
						return (TEnum)Enum.ToObject(_EnumType, int32);
					}
					break;
				case TypeCode.Int64:
					if (reader.TryGetInt64(out long int64))
					{
						return (TEnum)Enum.ToObject(_EnumType, int64);
					}
					break;
				case TypeCode.Int16:
					if (reader.TryGetInt16(out short int16))
					{
						return (TEnum)Enum.ToObject(_EnumType, int16);
					}
					break;
				case TypeCode.Byte:
					if (reader.TryGetByte(out byte ubyte8))
					{
						return (TEnum)Enum.ToObject(_EnumType, ubyte8);
					}
					break;
				case TypeCode.UInt32:
					if (reader.TryGetUInt32(out uint uint32))
					{
						return (TEnum)Enum.ToObject(_EnumType, uint32);
					}
					break;
				case TypeCode.UInt64:
					if (reader.TryGetUInt64(out ulong uint64))
					{
						return (TEnum)Enum.ToObject(_EnumType, uint64);
					}
					break;
				case TypeCode.UInt16:
					if (reader.TryGetUInt16(out ushort uint16))
					{
						return (TEnum)Enum.ToObject(_EnumType, uint16);
					}
					break;
				case TypeCode.SByte:
					if (reader.TryGetSByte(out sbyte byte8))
					{
						return (TEnum)Enum.ToObject(_EnumType, byte8);
					}
					break;
			}

			throw ThrowHelper.GenerateJsonException_DeserializeUnableToConvertValue(_EnumType);
		}

		public void Write(Utf8JsonWriter writer, TEnum value)
		{
			if (_RawToTransformed.TryGetValue(value, out EnumInfo? enumInfo))
			{
				writer.WriteStringValue(enumInfo.Name);
				return;
			}

			ulong rawValue = GetEnumValue(value);

			if (_IsFlags)
			{
				ulong calculatedValue = 0;

				StringBuilder Builder = new StringBuilder();
				foreach (KeyValuePair<TEnum, EnumInfo> enumItem in _RawToTransformed)
				{
					enumInfo = enumItem.Value;
					if (!value.HasFlag(enumInfo.EnumValue)
						|| enumInfo.RawValue == 0) // Definitions with 'None' should hit the cache case.
					{
						continue;
					}

					// Track the value to make sure all bits are represented.
					calculatedValue |= enumInfo.RawValue;

					if (Builder.Length > 0)
						Builder.Append(", ");
					Builder.Append(enumInfo.Name);
				}
				if (calculatedValue == rawValue)
				{
					string finalName = Builder.ToString();
					if (_RawToTransformed.Count < 64)
					{
						_RawToTransformed[value] = new EnumInfo(finalName, value, rawValue);
					}
					writer.WriteStringValue(finalName);
					return;
				}
			}

			if (!_AllowIntegerValues)
				throw new JsonException($"Enum type {_EnumType} does not have a mapping for integer value '{rawValue.ToString(CultureInfo.CurrentCulture)}'.");

			switch (_EnumTypeCode)
			{
				case TypeCode.Int32:
					writer.WriteNumberValue((int)rawValue);
					break;
				case TypeCode.Int64:
					writer.WriteNumberValue((long)rawValue);
					break;
				case TypeCode.Int16:
					writer.WriteNumberValue((short)rawValue);
					break;
				case TypeCode.Byte:
					writer.WriteNumberValue((byte)rawValue);
					break;
				case TypeCode.UInt32:
					writer.WriteNumberValue((uint)rawValue);
					break;
				case TypeCode.UInt64:
					writer.WriteNumberValue(rawValue);
					break;
				case TypeCode.UInt16:
					writer.WriteNumberValue((ushort)rawValue);
					break;
				case TypeCode.SByte:
					writer.WriteNumberValue((sbyte)rawValue);
					break;
				default:
					throw new JsonException(); // GetEnumValue should have already thrown.
			}
		}

		private ulong GetEnumValue(object value)
		{
			return _EnumTypeCode switch
			{
				TypeCode.Int32 => (ulong)(int)value,
				TypeCode.Int64 => (ulong)(long)value,
				TypeCode.Int16 => (ulong)(short)value,
				TypeCode.Byte => (byte)value,
				TypeCode.UInt32 => (uint)value,
				TypeCode.UInt64 => (ulong)value,
				TypeCode.UInt16 => (ushort)value,
				TypeCode.SByte => (ulong)(sbyte)value,
				_ => throw new NotSupportedException($"Enum '{value}' of {_EnumTypeCode} type is not supported."),
			};
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private TEnum ReturnDefaultValueOrThrowJsonException(string propertyValue)
			=> _DeserializationFailureFallbackValue ?? throw ThrowHelper.GenerateJsonException_DeserializeUnableToConvertValue(_EnumType, propertyValue);
	}
}
