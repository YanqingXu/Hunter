using System;
using UnityEngine;

namespace BigWorld.Map2D
{
    public enum MapTilePropertyKind { Boolean, Integer, Number, Text }

    public static class MapTilePropertyKeys
    {
        public const string Destructible = "destructible";
        public const string Flammable = "flammable";
    }

    /// <summary>A serializable, typed value identified by a stable, case-sensitive key.</summary>
    [Serializable]
    public class MapTilePropertyValue
    {
        [SerializeField] private string key = string.Empty;
        [SerializeField] private MapTilePropertyKind kind;
        [SerializeField] private bool boolValue;
        [SerializeField] private int intValue;
        [SerializeField] private float numberValue;
        [SerializeField] private string textValue = string.Empty;

        public string Key { get { return key ?? string.Empty; } set { key = value ?? string.Empty; } }
        public MapTilePropertyKind Kind { get { return kind; } set { kind = value; } }
        public bool BoolValue { get { return boolValue; } set { boolValue = value; } }
        public int IntValue { get { return intValue; } set { intValue = value; } }
        public float NumberValue { get { return numberValue; } set { numberValue = value; } }
        public string TextValue { get { return textValue ?? string.Empty; } set { textValue = value ?? string.Empty; } }

        public MapTilePropertyValue Clone()
        {
            return new MapTilePropertyValue
            {
                Key = Key,
                Kind = Kind,
                BoolValue = BoolValue,
                IntValue = IntValue,
                NumberValue = NumberValue,
                TextValue = TextValue
            };
        }

        internal bool SameTypedValue(MapTilePropertyValue other)
        {
            if (other == null || !string.Equals(Key, other.Key, StringComparison.Ordinal) || Kind != other.Kind) return false;
            switch (Kind)
            {
                case MapTilePropertyKind.Boolean: return BoolValue == other.BoolValue;
                case MapTilePropertyKind.Integer: return IntValue == other.IntValue;
                case MapTilePropertyKind.Number: return NumberValue.Equals(other.NumberValue);
                case MapTilePropertyKind.Text: return string.Equals(TextValue, other.TextValue, StringComparison.Ordinal);
                default: return false;
            }
        }
    }

    /// <summary>The inherited typed value is this property's schema default.</summary>
    [Serializable]
    public sealed class MapTilePropertyDefinition : MapTilePropertyValue
    {
        [SerializeField] private string displayName = "新属性";

        public string DisplayName { get { return string.IsNullOrEmpty(displayName) ? Key : displayName; } set { displayName = value ?? string.Empty; } }

        public MapTilePropertyDefinition()
        {
            Key = Guid.NewGuid().ToString("N");
        }
    }
}
