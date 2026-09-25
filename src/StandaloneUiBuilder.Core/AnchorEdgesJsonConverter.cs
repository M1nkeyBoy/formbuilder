using System.Text.Json;
using System.Text.Json.Serialization;

namespace StandaloneUiBuilder.Core;

/// <summary>Stores anchors as a readable list of edge names: <c>["left", "top"]</c>.</summary>
internal sealed class AnchorEdgesJsonConverter : JsonConverter<AnchorEdges>
{
    private static readonly (AnchorEdges Edge, string Name)[] Edges =
    [
        (AnchorEdges.Left, "left"),
        (AnchorEdges.Top, "top"),
        (AnchorEdges.Right, "right"),
        (AnchorEdges.Bottom, "bottom"),
    ];

    public override AnchorEdges Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("An anchor must be a list of edges such as [\"left\", \"top\"].");
        }

        var anchor = AnchorEdges.None;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            var match = Edges.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null)
            {
                // Not a JsonException, so the reader passes this message through unchanged.
                throw new ProjectFileException($"\"{name}\" is not an anchor edge. Use left, top, right or bottom.");
            }

            anchor |= match.Edge;
        }

        return anchor;
    }

    public override void Write(Utf8JsonWriter writer, AnchorEdges value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var (edge, name) in Edges)
        {
            if (value.HasFlag(edge))
            {
                writer.WriteStringValue(name);
            }
        }

        writer.WriteEndArray();
    }
}
