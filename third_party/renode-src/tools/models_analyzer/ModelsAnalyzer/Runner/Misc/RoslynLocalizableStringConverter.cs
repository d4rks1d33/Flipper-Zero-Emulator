//
// Copyright (c) 2022-2023 Antmicro
//
// This file is licensed under the Apache License 2.0.
// Full license text is available in 'LICENSE'.
//
using Microsoft.CodeAnalysis;
using Newtonsoft.Json;

namespace Runner;

// Adapted from: https://www.newtonsoft.com/json/help/html/CustomJsonConverter.htm
public class RoslynLocalizableStringConverter : JsonConverter
{
    private readonly Type type = typeof(LocalizableString);

    public RoslynLocalizableStringConverter()
    { }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        writer.WriteValue(value?.ToString());
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException($"Deserializing {type.Name} is unimplemented.");
    }

    public override bool CanRead
    {
        get => false;
    }

    public override bool CanConvert(Type objectType)
    {
        return type.IsAssignableFrom(objectType);
    }
}