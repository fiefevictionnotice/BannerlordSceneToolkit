using System;
using System.Linq;
using TaleWorlds.Library;

namespace BannerlordSceneToolkit
{
    // Delegates to TaleWorlds.Library.Color rather than reimplementing hex<->uint packing -
    // its ToUnsignedInteger() packs as 0xAARRGGBB (alpha in the high byte), which does NOT match
    // a naive left-to-right hex-to-uint parse of "RRGGBBAA" text. Using the engine's own
    // conversion guarantees whatever hex you type produces the same color the game itself would
    // produce for that string.
    public static class ColorHex
    {
        // Deliberately narrow list, chosen to read as an obvious "reset this" gesture rather than
        // something a real hex code could collide with by accident.
        private static readonly string[] ClearSentinels = { "#", "blank", "clear", "white", "none" };

        public static bool TryParse(string text, out uint value)
        {
            value = 0;

            // A genuinely empty box (nothing ever typed) means "leave this alone" everywhere it's
            // used - it must NOT resolve to a color, or every untouched color field would silently
            // clear whatever's already there. A sentinel word/symbol, or a bare space, is the
            // explicit "I want to reset this to no tint" gesture instead - text.Length > 0 is what
            // separates the two, since IsNullOrEmpty(" ") is false but the box still has content.
            if (string.IsNullOrEmpty(text)) return false;

            var trimmed = text.Trim();
            bool isClearSentinel = trimmed.Length == 0 || ClearSentinels.Contains(trimmed.ToLowerInvariant());
            if (isClearSentinel)
            {
                value = Color.ConvertStringToColor("#FFFFFFFF").ToUnsignedInteger();
                return true;
            }

            var hex = trimmed;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            if (hex.Length == 7) hex += "FF"; // #RRGGBB, no alpha given - assume fully opaque
            if (hex.Length != 9) return false;

            foreach (var c in hex.Substring(1))
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }

            value = Color.ConvertStringToColor(hex).ToUnsignedInteger();
            return true;
        }

        public static string ToHex(uint value) => Color.FromUint(value).ToString();

        // Per-channel multiply (0xAARRGGBB packing, matches ToUnsignedInteger() above) - this is
        // how the engine actually composites a mesh's own Color together with a MetaMesh's Factor1
        // or a GameEntity's FactorColor at render time (each is a multiplicative tint layered on
        // top of the last, not a replacement). White (0xFFFFFFFF) is the identity value for this -
        // multiplying by it is always a no-op, so callers can pass a possibly-white factor here
        // unconditionally without needing their own "skip if white" branch first.
        public static uint Multiply(uint a, uint b)
        {
            byte MulByte(uint x, uint y, int shift)
            {
                var xb = (byte)((x >> shift) & 0xFF);
                var yb = (byte)((y >> shift) & 0xFF);
                return (byte)(xb * yb / 255);
            }

            uint result = 0;
            result |= (uint)MulByte(a, b, 24) << 24; // A
            result |= (uint)MulByte(a, b, 16) << 16; // R
            result |= (uint)MulByte(a, b, 8) << 8;   // G
            result |= (uint)MulByte(a, b, 0);        // B
            return result;
        }

        // COLOR SPACE (2026-11-12, "it always reduces the saturation of your color factor ... it
        // seems almost exactly 50%"). Mesh.Color is stored LINEAR. The editor's own colour field
        // converts the sRGB hex you type to linear before storing; the toolkit wrote the typed
        // hex raw, so the renderer brightened it a second time. Measured live: #BAFFAF came out
        // #DDFFD7 and #802020 came out #BB6363 (a 2.2 gamma predicts #DDFFD7 / #BA6363), while
        // the same hex typed into the editor's field stays as typed. ("50% to white" fit the light
        // colour too, but predicted #C09090 for the dark one.)
        //
        // Rule: a hex a PERSON typed or reads is sRGB; anything stored, logged for undo, copied
        // between meshes or saved in a colour preset stays the raw stored (linear) value. So
        // convert exactly at the typed-input -> mesh write and at mesh -> hex shown for re-use.
        // Alpha is never converted. 8-bit linear storage loses a little precision on very dark
        // colours; the editor's own field has the same limit.
        private const double Gamma = 2.2;

        private static uint MapRgb(uint value, Func<double, double> f)
        {
            uint Ch(int shift)
            {
                var c = ((value >> shift) & 0xFF) / 255.0;
                return (uint)Math.Max(0, Math.Min(255, Math.Round(f(c) * 255.0)));
            }
            return (value & 0xFF000000u) | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
        }

        public static uint SrgbToLinear(uint srgb) => MapRgb(srgb, c => Math.Pow(c, Gamma));
        public static uint LinearToSrgb(uint linear) => MapRgb(linear, c => Math.Pow(c, 1.0 / Gamma));
    }
}
