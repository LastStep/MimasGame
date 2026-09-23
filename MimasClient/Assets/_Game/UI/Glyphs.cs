using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mimas.Client.UI
{
    /// <summary>
    /// The interface language's glyphs (docs/ui/language.md §3), drawn with <see cref="Painter2D"/> rather
    /// than imported: no package, no icon files, one path table that serves 12px and 220px alike. Each shape
    /// is authored on a 24-unit grid in SVG path syntax — the strings are the design mock's own
    /// (<c>artifacts/UI Drafts/hud-mock/gen*.py</c>, <c>ICON</c>) — and scaled to the element's layout.
    /// Stroke 1.5 units, round caps and joins. Colour comes from the <c>--glyph-color</c> custom property so a
    /// USS rule decides it from a <c>--mimas-*</c> token; the class <c>glyph--filled</c> fills a closed shape
    /// (a held egg, the starting Blessing's circle).
    /// <para>
    /// One element with a <see cref="Shape"/> key rather than one class per glyph: the drawing is the same
    /// code for all of them, and a UXML author writes <c>&lt;Mimas.Client.UI.Glyph shape="heart" /&gt;</c>.
    /// </para>
    /// </summary>
    [UxmlElement]
    public partial class Glyph : VisualElement
    {
        public const string FilledClass = "glyph--filled";

        private static readonly CustomStyleProperty<Color> ColorProperty = new CustomStyleProperty<Color>("--glyph-color");
        private static readonly CustomStyleProperty<float> StrokeProperty = new CustomStyleProperty<float>("--glyph-stroke");

        private const float Grid = 24f;
        private const float DefaultStroke = 1.5f;

        private string _shape;
        private Color _color = Color.white;
        private float _stroke = DefaultStroke;
        private Label _question;

        /// <summary>The shape key: one of <see cref="GlyphPaths.Keys"/>.</summary>
        [UxmlAttribute]
        public string Shape
        {
            get => _shape;
            set
            {
                if (_shape == value) return;
                _shape = value;
                SyncQuestionMark();
                MarkDirtyRepaint();
            }
        }

        public Glyph() : this(null)
        {
        }

        public Glyph(string shape)
        {
            AddToClassList("glyph");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
            Shape = shape;
        }

        public bool Filled
        {
            get => ClassListContains(FilledClass);
            set { EnableInClassList(FilledClass, value); MarkDirtyRepaint(); }
        }

        private Color? _tint;

        /// <summary>
        /// A colour from data rather than from a token — the lineage's painting hue on the emblem. Null goes
        /// back to <c>--glyph-color</c>. UI Toolkit cannot set a custom property inline, hence this.
        /// </summary>
        public Color? Tint
        {
            get => _tint;
            set
            {
                _tint = value;
                if (_question != null) _question.style.color = _tint ?? _color;
                MarkDirtyRepaint();
            }
        }

        private void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            Color color;
            if (evt.customStyle.TryGetValue(ColorProperty, out color)) _color = color;
            float stroke;
            _stroke = evt.customStyle.TryGetValue(StrokeProperty, out stroke) ? stroke : DefaultStroke;
            if (_question != null) _question.style.color = _tint ?? _color;
            MarkDirtyRepaint();
        }

        /// <summary>The unknown glyph is a dashed circle with a "?" set in type, so it follows the font.</summary>
        private void SyncQuestionMark()
        {
            bool wants = _shape == GlyphPaths.Unknown;
            if (wants && _question == null)
            {
                _question = new Label("?") { pickingMode = PickingMode.Ignore };
                _question.AddToClassList("glyph-question");
                _question.style.color = _color;
                Add(_question);
            }
            else if (!wants && _question != null)
            {
                _question.RemoveFromHierarchy();
                _question = null;
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (string.IsNullOrEmpty(_shape) || rect.width <= 0f || rect.height <= 0f) return;

            float size = Mathf.Min(rect.width, rect.height);
            float scale = size / Grid;
            var origin = new Vector2(rect.x + (rect.width - size) * 0.5f, rect.y + (rect.height - size) * 0.5f);

            Painter2D painter = context.painter2D;
            Color color = _tint ?? _color;
            painter.strokeColor = color;
            painter.fillColor = color;
            // A floor of 1.1 panel units: at 11px a 1.5/24 stroke is 0.7px, and at a 1280×720 window the HUD
            // draws at two thirds of that, which read as a grey smudge rather than a line.
            painter.lineWidth = Mathf.Max(1.1f, _stroke * scale);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            bool filled = Filled;
            GlyphPaths.Draw(_shape, painter, origin, scale, filled);
        }
    }

    /// <summary>
    /// A dashed rectangle the size of the element: the unseen tile's edge (language §5 <c>state.unseen</c>).
    /// USS borders cannot be dashed. Colour from <c>--glyph-color</c>.
    /// </summary>
    [UxmlElement]
    public partial class DashedFrame : VisualElement
    {
        private static readonly CustomStyleProperty<Color> ColorProperty = new CustomStyleProperty<Color>("--glyph-color");

        private const float Dash = 4f;
        private const float Gap = 3f;

        private Color _color = Color.white;

        public DashedFrame()
        {
            AddToClassList("dashed-frame");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(evt =>
            {
                Color color;
                if (evt.customStyle.TryGetValue(ColorProperty, out color)) _color = color;
                MarkDirtyRepaint();
            });
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect r = contentRect;
            if (r.width <= 1f || r.height <= 1f) return;
            Painter2D painter = context.painter2D;
            painter.strokeColor = _color;
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Butt;
            float x0 = r.xMin + 0.5f, y0 = r.yMin + 0.5f, x1 = r.xMax - 0.5f, y1 = r.yMax - 0.5f;
            Side(painter, new Vector2(x0, y0), new Vector2(x1, y0));
            Side(painter, new Vector2(x1, y0), new Vector2(x1, y1));
            Side(painter, new Vector2(x1, y1), new Vector2(x0, y1));
            Side(painter, new Vector2(x0, y1), new Vector2(x0, y0));
        }

        private static void Side(Painter2D painter, Vector2 from, Vector2 to)
        {
            float length = Vector2.Distance(from, to);
            Vector2 direction = (to - from) / length;
            for (float at = 0f; at < length; at += Dash + Gap)
            {
                painter.BeginPath();
                painter.MoveTo(from + direction * at);
                painter.LineTo(from + direction * Mathf.Min(length, at + Dash));
                painter.Stroke();
            }
        }
    }

    /// <summary>
    /// The path table and a small SVG path interpreter (M L H V C S Q A Z, absolute and relative) that
    /// turns each entry into <see cref="Painter2D"/> calls. Circles are listed separately because the mock
    /// drew them as <c>&lt;circle&gt;</c> elements. Every arc in the table is circular, which is all
    /// <see cref="Painter2D.Arc"/> can draw; an elliptical one would draw with the larger radius.
    /// </summary>
    public static class GlyphPaths
    {
        public const string Unknown = "unknown";

        private struct Circle
        {
            public float X, Y, R;
            public bool Solid;

            public Circle(float x, float y, float r, bool solid = false)
            {
                X = x; Y = y; R = r; Solid = solid;
            }
        }

        private sealed class Entry
        {
            public string Path;
            public Circle[] Circles;

            /// <summary>Closed shapes that <c>glyph--filled</c> fills rather than strokes.</summary>
            public bool Fillable;
        }

        private static readonly Dictionary<string, Entry> Table = new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            // Vitals.
            { "heart", P("M12 20s-7-4.5-7-10a4 4 0 0 1 7-2.5A4 4 0 0 1 19 10c0 5.5-7 10-7 10z", fillable: true) },
            { "bolt", P("M13 2L4 14h7l-1 8 9-12h-7z", fillable: true) },
            { "egg", C(true, new Circle(12, 12, 7)) },

            // Boon kinds: circle, diamond, triangle (hollow, or filled for the starting Blessing).
            { "blessing", C(true, new Circle(12, 12, 7)) },
            { "enchant", P("M12 4l8 8-8 8-8-8z", fillable: true) },
            { "sigil", P("M12 4.5l8 14H4z", fillable: true) },

            // Lanes and slots.
            { "sword", P("M14 4l6 6-9 9-6-6zM5 13l-2 2 6 6 2-2M15 9l-6 6") },
            { "spark", P("M12 3v4M12 17v4M3 12h4M17 12h4M6 6l2.5 2.5M15.5 15.5L18 18M6 18l2.5-2.5M15.5 8.5L18 6") },
            { "crown", P("M3 18h18l-1-10-5 4-3-6-3 6-5-4z") },
            { "boot", P("M5 4h6v8l8 4v3H5z") },
            { "shield", P("M12 3l7 3v6c0 4-3 7-7 9-4-2-7-5-7-9V6z") },

            // Trajectory and sight.
            { "arc", P("M3 18c3-10 15-10 18 0") },
            { "straight", P("M3 12h18M17 8l4 4-4 4") },
            { "eye", P("M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z", new Circle(12, 12, 3)) },
            { "eye-struck", P("M3 3l18 18M10 6c.7-.1 1.3-.2 2-.2 6 0 10 6 10 6s-1 1.6-2.8 3.2M6.5 6.5C4 8.3 2 12 2 12s4 7 10 7c1.6 0 3-.4 4.3-1") },

            // Lineage emblems (placeholders until art: docs/ui/examine.md §6).
            { "laurel", P("M12 20V6M12 6c-3 0-5 2-5 5 2 0 4-1 5-3M12 6c3 0 5 2 5 5-2 0-4-1-5-3M12 12c-3 0-5 2-5 5 2 0 4-1 5-3M12 12c3 0 5 2 5 5-2 0-4-1-5-3") },
            { "hammer", P("M4 20l7-7M9 8l4-4 7 7-4 4zM11 6l7 7") },
            { "lotus", P("M12 21c-5 0-8-3-8-7 3 0 5 1 6 3-1-3 0-6 2-8 2 2 3 5 2 8 1-2 3-3 6-3 0 4-3 7-8 7z") },

            // Chrome.
            { "close", P("M6 6l12 12M18 6L6 18") },

            // Action placeholders the mock drew (the tile shows a letter until action art exists).
            { "walk", P("M4 12h14M13 6l6 6-6 6") },
            { "jump", P("M3 18c3-10 15-10 18 0M15 13l5 4 1-6") },
            { "shot", P("M6 3c8 4 8 14 0 18M6 3l1 18M20 12H9M20 12l-4-3M20 12l-4 3") },
            { "aimed", P("M12 2v4M12 18v4M2 12h4M18 12h4", new Circle(12, 12, 7), new Circle(12, 12, 1.5f, true)) },
            { "fire", P("M12 3c1 4 5 5 5 10a5 5 0 0 1-10 0c0-3 2-4 2-6 1 1 2 2 3 1-1-2 0-4 0-5z") },
            { "blink", P("M12 2v3M12 19v3M2 12h3M19 12h3", new Circle(12, 12, 3)) },
        };

        /// <summary>Every shape key the table knows, plus <see cref="Unknown"/>.</summary>
        public static IEnumerable<string> Keys
        {
            get
            {
                foreach (string key in Table.Keys) yield return key;
                yield return Unknown;
            }
        }

        public static bool Has(string shape) => shape == Unknown || (shape != null && Table.ContainsKey(shape));

        private static Entry P(string path, params Circle[] circles) => new Entry { Path = path, Circles = circles };

        private static Entry P(string path, bool fillable) => new Entry { Path = path, Circles = new Circle[0], Fillable = fillable };

        private static Entry C(bool fillable, params Circle[] circles) => new Entry { Path = null, Circles = circles, Fillable = fillable };

        public static void Draw(string shape, Painter2D painter, Vector2 origin, float scale, bool filled)
        {
            if (shape == Unknown)
            {
                DrawDashedCircle(painter, origin + new Vector2(12f, 12f) * scale, 9.5f * scale, 12);
                return;
            }

            Entry entry;
            if (!Table.TryGetValue(shape, out entry)) return;

            bool fill = filled && entry.Fillable;
            if (entry.Path != null)
            {
                painter.BeginPath();
                Trace(entry.Path, painter, origin, scale);
                if (fill) painter.Fill();
                painter.Stroke();
            }

            for (int i = 0; i < entry.Circles.Length; i++)
            {
                Circle c = entry.Circles[i];
                painter.BeginPath();
                painter.Arc(origin + new Vector2(c.X, c.Y) * scale, c.R * scale, 0f, 360f);
                painter.ClosePath();
                if (c.Solid || fill) painter.Fill();
                if (!c.Solid) painter.Stroke();
            }
        }

        private static void DrawDashedCircle(Painter2D painter, Vector2 centre, float radius, int dashes)
        {
            float step = 360f / dashes;
            for (int i = 0; i < dashes; i++)
            {
                painter.BeginPath();
                painter.Arc(centre, radius, i * step, i * step + step * 0.55f);
                painter.Stroke();
            }
        }

        // ---- the SVG path interpreter ----------------------------------------------------------------

        private static void Trace(string d, Painter2D painter, Vector2 origin, float scale)
        {
            var reader = new PathReader(d);
            Vector2 current = Vector2.zero, start = Vector2.zero, lastControl = Vector2.zero;
            char command = 'M';
            char previous = ' ';

            while (reader.More())
            {
                if (reader.PeekCommand()) command = reader.ReadCommand();
                else if (command == 'M') command = 'L';           // implicit lineto after a moveto
                else if (command == 'm') command = 'l';

                bool relative = char.IsLower(command);
                Vector2 basePoint = relative ? current : Vector2.zero;

                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        current = basePoint + reader.Point();
                        start = current;
                        painter.MoveTo(Map(current, origin, scale));
                        break;
                    case 'L':
                        current = basePoint + reader.Point();
                        painter.LineTo(Map(current, origin, scale));
                        break;
                    case 'H':
                    {
                        float x = reader.Number();
                        current = new Vector2(relative ? current.x + x : x, current.y);
                        painter.LineTo(Map(current, origin, scale));
                        break;
                    }
                    case 'V':
                    {
                        float y = reader.Number();
                        current = new Vector2(current.x, relative ? current.y + y : y);
                        painter.LineTo(Map(current, origin, scale));
                        break;
                    }
                    case 'C':
                    {
                        Vector2 c1 = basePoint + reader.Point();
                        Vector2 c2 = basePoint + reader.Point();
                        Vector2 end = basePoint + reader.Point();
                        painter.BezierCurveTo(Map(c1, origin, scale), Map(c2, origin, scale), Map(end, origin, scale));
                        lastControl = c2;
                        current = end;
                        break;
                    }
                    case 'S':
                    {
                        char prev = char.ToUpperInvariant(previous);
                        Vector2 c1 = prev == 'C' || prev == 'S' ? current * 2f - lastControl : current;
                        Vector2 c2 = basePoint + reader.Point();
                        Vector2 end = basePoint + reader.Point();
                        painter.BezierCurveTo(Map(c1, origin, scale), Map(c2, origin, scale), Map(end, origin, scale));
                        lastControl = c2;
                        current = end;
                        break;
                    }
                    case 'Q':
                    {
                        Vector2 c = basePoint + reader.Point();
                        Vector2 end = basePoint + reader.Point();
                        painter.QuadraticCurveTo(Map(c, origin, scale), Map(end, origin, scale));
                        lastControl = c;
                        current = end;
                        break;
                    }
                    case 'A':
                    {
                        float rx = reader.Number();
                        float ry = reader.Number();
                        reader.Number();                                   // x-axis rotation: circles only
                        bool large = reader.Flag();
                        bool sweep = reader.Flag();
                        Vector2 end = basePoint + reader.Point();
                        ArcTo(painter, current, end, Mathf.Max(rx, ry), large, sweep, origin, scale);
                        current = end;
                        break;
                    }
                    case 'Z':
                        painter.ClosePath();
                        current = start;
                        break;
                    default:
                        return;                                            // unknown command: stop, draw what we have
                }
                previous = command;
            }
        }

        /// <summary>SVG's endpoint arc as a centre arc (W3C SVG implementation notes F.6.5), circular case.</summary>
        private static void ArcTo(Painter2D painter, Vector2 from, Vector2 to, float r, bool large, bool sweep, Vector2 origin, float scale)
        {
            if (r <= 0f || from == to)
            {
                painter.LineTo(Map(to, origin, scale));
                return;
            }

            Vector2 mid = (from - to) * 0.5f;
            float d2 = mid.sqrMagnitude;
            if (d2 > r * r) r = Mathf.Sqrt(d2);                           // radius too small: scale up (F.6.6)
            float factor = Mathf.Sqrt(Mathf.Max(0f, (r * r - d2) / d2));
            if (large == sweep) factor = -factor;
            Vector2 centreOffset = new Vector2(mid.y, -mid.x) * factor;
            Vector2 centre = centreOffset + (from + to) * 0.5f;

            float a0 = Mathf.Atan2(from.y - centre.y, from.x - centre.x) * Mathf.Rad2Deg;
            float a1 = Mathf.Atan2(to.y - centre.y, to.x - centre.x) * Mathf.Rad2Deg;

            // y points down in both SVG and UI Toolkit, so SVG's positive sweep is clockwise on screen.
            painter.Arc(Map(centre, origin, scale), r * scale, a0, a1, sweep ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
        }

        private static Vector2 Map(Vector2 point, Vector2 origin, float scale) => origin + point * scale;

        /// <summary>Numbers, flags and command letters out of an SVG path string, SVG's compact number rules included.</summary>
        private struct PathReader
        {
            private readonly string _d;
            private int _i;

            public PathReader(string d)
            {
                _d = d;
                _i = 0;
            }

            public bool More()
            {
                SkipSeparators();
                return _i < _d.Length;
            }

            public bool PeekCommand()
            {
                SkipSeparators();
                return _i < _d.Length && char.IsLetter(_d[_i]) && _d[_i] != 'e' && _d[_i] != 'E';
            }

            public char ReadCommand()
            {
                SkipSeparators();
                return _d[_i++];
            }

            public Vector2 Point()
            {
                float x = Number();
                float y = Number();
                return new Vector2(x, y);
            }

            /// <summary>An arc flag is a single 0 or 1 that may run straight into the next number ("a4 4 0 0 1 7-2.5").</summary>
            public bool Flag()
            {
                SkipSeparators();
                if (_i >= _d.Length) return false;
                return _d[_i++] == '1';
            }

            public float Number()
            {
                SkipSeparators();
                int start = _i;
                if (_i < _d.Length && (_d[_i] == '-' || _d[_i] == '+')) _i++;
                bool dot = false;
                while (_i < _d.Length)
                {
                    char c = _d[_i];
                    if (char.IsDigit(c)) { _i++; continue; }
                    if (c == '.' && !dot) { dot = true; _i++; continue; }
                    break;
                }
                if (_i < _d.Length && (_d[_i] == 'e' || _d[_i] == 'E'))
                {
                    _i++;
                    if (_i < _d.Length && (_d[_i] == '-' || _d[_i] == '+')) _i++;
                    while (_i < _d.Length && char.IsDigit(_d[_i])) _i++;
                }
                if (_i == start) { _i++; return 0f; }                    // malformed: skip a character
                return float.Parse(_d.Substring(start, _i - start), System.Globalization.CultureInfo.InvariantCulture);
            }

            private void SkipSeparators()
            {
                while (_i < _d.Length && (_d[_i] == ' ' || _d[_i] == ',' || _d[_i] == '\n' || _d[_i] == '\t' || _d[_i] == '\r')) _i++;
            }
        }
    }
}
