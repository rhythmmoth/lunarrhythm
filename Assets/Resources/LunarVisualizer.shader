// LunarRhythm's in-game visualizer (board NextVisualizers): a night sky behind the highway that plays along with the
// song. Drawn on one full-screen RawImage; MoonVisualizer feeds it the beat, bar, section energy, star power and the
// cover's colours. Styles: 0 Tide, 1 Pulse, 2 Meteor, 3 Aurora. Colours are mixed in sRGB and converted at the end.
Shader "LunarRhythm/Visualizer"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _ColA ("Sky tint", Color) = (0.106, 0.165, 0.42, 1)
        _ColB ("Accent", Color) = (0.49, 0.59, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            float4 _ColA, _ColB;
            // _T song time (s), _Beat/_Down decays since the last beat/bar, _Bar bars so far (fraction = place in bar),
            // _BarLen seconds per bar, _Energy section brightness, _Gold star power, _Streak shooting star 0-1 (>1 none)
            float _T, _Beat, _Down, _Bar, _BarLen, _Energy, _Gold, _Streak, _Style, _Intensity, _Aspect;

            static const float3 NIGHT = float3(0.008, 0.035, 0.10);
            static const float3 CREAM = float3(0.953, 0.91, 0.80);

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float stars(float2 p, float density)
            {
                float2 g = floor(p * density);
                float2 f = frac(p * density) - 0.5;
                float h = hash(g);
                float2 o = float2(hash(g + 1.7), hash(g + 3.1)) - 0.5;
                float d = length(f - o * 0.6);
                float twinkle = 0.65 + 0.35 * sin(_T * 1.3 + h * 40.0);
                return step(0.85, h) * smoothstep(0.09, 0.0, d) * twinkle;
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1.0, 0.0)), u.x), lerp(hash(i + float2(0.0, 1.0)), hash(i + 1.0), u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int o = 0; o < 4; o++)
                {
                    v += a * vnoise(p);
                    p *= 2.03;
                    a *= 0.5;
                }
                return v;
            }

            float segment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                return length(pa - ba * h);
            }

            // A glowing trail whose head is at `head`, moving along `dir`
            float trail(float2 p, float2 head, float2 dir, float len)
            {
                float2 q = p - head;
                float along = dot(q, -dir);
                float perp = dot(q, float2(-dir.y, dir.x));
                float body = step(0.0, along) * step(along, len) * (1.0 - along / len);
                return body * exp(-perp * perp / 0.0000045) + exp(-dot(q, q) / 0.00002);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 p = float2(uv.x * _Aspect, uv.y);
                float3 tint = LinearToGammaSpace(_ColA.rgb);
                float3 accent = LinearToGammaSpace(_ColB.rgb);
                int style = (int) round(_Style);
                float k = _Intensity;

                // The sky: night, warmed from below by the cover's colour, brighter in choruses
                float3 col = lerp(NIGHT, tint * 0.45, saturate(1.0 - uv.y) * 0.85);
                if (style == 2) col = lerp(col, float3(0.10, 0.04, 0.16), 0.5 * (1.0 - uv.y)); // Meteor: a darker, warmer sky
                else if (style == 5) col = lerp(col, float3(0.16, 0.03, 0.12), 0.5 * (1.0 - uv.y)); // Shatter: pink dusk
                else if (style == 6) col = lerp(col, float3(0.20, 0.04, 0.26), saturate(1.0 - (uv.y - 0.42) / 0.45) * 0.8); // Grid: purple dusk
                else if (style == 8) col = lerp(col, float3(0.10, 0.03, 0.08), 0.55); // Glow: warm
                else if (style == 9) col = lerp(col, float3(0.01, 0.04, 0.12), 0.5); // Smoke: deep blue
                col *= 0.7 + 0.45 * _Energy;
                col += stars(p, 70.0) * 0.75 + stars(p + 13.0, 140.0) * 0.4;

                // The moon, in the open sky left of the lyrics (board NextMoonHighway), clear of the highway and its notes
                float2 m = float2(0.14 * _Aspect, 0.8);
                float radius = (style == 1 ? 0.06 : style == 6 ? 0.07 : 0.045) * (1.0 + (style == 1 ? 0.08 : 0.03) * _Beat * k);
                float d = length(p - m);
                float3 moonCol = lerp(CREAM, float3(1.0, 0.82, 0.42), _Gold);
                float disc = smoothstep(radius, radius - (style == 9 ? radius * 0.3 : 0.003), d); // Smoke: a hazy edge
                float3 discCol = moonCol;
                if (style == 6) // Grid: a striped sunset moon
                {
                    discCol = lerp(float3(1.0, 0.36, 0.72), float3(1.0, 0.78, 0.42), saturate((p.y - m.y) / radius * 0.5 + 0.5));
                    disc *= (p.y > m.y || frac((m.y - p.y) * 60.0) > 0.35) ? 1.0 : 0.0;
                }
                col += discCol * exp(-d * d / (radius * radius * 6.0)) * (0.28 + 0.25 * _Beat * k);
                col = lerp(col, discCol, disc);

                if (style == 0) // Tide: three layers of waves, rolling once per bar
                {
                    for (int l = 2; l >= 0; l--)
                    {
                        float fl = l;
                        float dirn = l == 1 ? -1.0 : 1.0;
                        float y0 = 0.10 + 0.09 * fl;
                        float amp = (0.016 + 0.008 * fl) * (0.8 + 0.4 * _Energy);
                        float w = y0 + amp * sin(p.x * (5.0 - fl) + _Bar * 6.2832 * dirn * (0.5 + 0.2 * fl));
                        float inside = smoothstep(w + 0.004, w - 0.004, uv.y);
                        float3 water = lerp(tint * 0.9, NIGHT, 0.25 + 0.25 * (2.0 - fl));
                        float crest = exp(-pow((uv.y - w) / 0.003, 2.0)) * (0.25 + 0.5 * _Beat * k);
                        col = lerp(col, water, inside) + moonCol * crest * 0.5;
                    }
                    // The moon's reflection on the water
                    float shimmer = exp(-pow((p.x - m.x) / 0.05, 2.0)) * step(uv.y, 0.3) * (0.5 + 0.5 * sin(uv.y * 160.0 - _T * 3.0));
                    col += moonCol * shimmer * 0.12 * k;
                }
                else if (style == 1) // Pulse: a ring leaves the moon every bar
                {
                    for (int r = 0; r < 2; r++)
                    {
                        float age = frac(_Bar) + r;
                        float ringR = radius + age * 0.5;
                        float ring = exp(-pow((d - ringR) / 0.005, 2.0)) * (1.0 - age / 2.0);
                        col += lerp(accent, CREAM, 0.5) * ring * 0.7 * k;
                    }
                }
                else if (style == 2) // Meteor: streaks across the sky, faster with the tempo; downbeats flash
                {
                    float2 dir = normalize(float2(-1.0, -0.45));
                    float speed = 0.9 / max(_BarLen, 0.6);
                    for (int s = 0; s < 6; s++)
                    {
                        float fs = s;
                        float phase = frac(_T * speed * 0.6 + hash(float2(fs, 7.0)));
                        float2 start = float2(hash(float2(fs, 3.0)) * _Aspect * 0.9 + 0.35, 1.05 + 0.1 * hash(float2(fs, 5.0)));
                        float2 head = start + dir * phase * 1.7;
                        float3 streakCol = lerp(CREAM, float3(1.0, 0.70, 0.54), hash(float2(fs, 9.0)));
                        col += streakCol * trail(p, head, dir, 0.22) * 0.8 * k;
                    }
                    col += accent * _Down * 0.12 * k;
                }
                else if (style == 4) // Searchlights: two stage beams sweeping, swinging once a bar
                {
                    for (int s = 0; s < 2; s++)
                    {
                        float side = s == 0 ? -1.0 : 1.0;
                        float2 v = p - float2((0.5 + 0.36 * side) * _Aspect, -0.05);
                        float ang = atan2(v.x, v.y);
                        float swing = -side * 0.3 + 0.42 * sin(_Bar * 3.14159 + s * 1.3);
                        float beam = exp(-pow((ang - swing) / (0.08 + 0.03 * _Energy), 2.0)) * saturate(1.0 - length(v) * 0.75);
                        col += lerp(CREAM, accent, 0.35 * s) * beam * (0.3 + 0.25 * _Beat) * k;
                    }
                }
                else if (style == 5) // Shatter: stars burst apart on each downbeat
                {
                    float barN = floor(_Bar);
                    float age = frac(_Bar);
                    for (int b = 0; b < 3; b++)
                    {
                        float fb = b;
                        float2 c = float2(hash(float2(barN, fb)) * _Aspect, 0.4 + 0.5 * hash(float2(fb, barN + 3.0)));
                        float2 q = p - c;
                        float dd = length(q);
                        float rays = pow(abs(cos(atan2(q.y, q.x) * 4.0 + fb)), 12.0);
                        float spark = exp(-pow((dd - age * (0.12 + 0.08 * fb)) / 0.006, 2.0)) * (0.4 + 0.6 * rays) * pow(1.0 - age, 2.0);
                        float3 pink = b == 1 ? CREAM : float3(1.0, 0.45, 0.63);
                        col += pink * (spark * 0.9 + exp(-dd * dd / 0.0004) * pow(1.0 - age, 6.0)) * k;
                    }
                    col += float3(1.0, 0.45, 0.63) * _Down * 0.08 * k;
                }
                else if (style == 6) // Grid: a neon floor rushing in, one line per beat
                {
                    float horizon = 0.42;
                    col += float3(1.0, 0.35, 0.78) * exp(-pow((uv.y - horizon) / 0.02, 2.0)) * 0.35 * k;
                    if (uv.y < horizon)
                    {
                        float depth = 1.0 / max(horizon - uv.y, 0.002);
                        float gx = (p.x - 0.5 * _Aspect) * depth * 0.35;
                        float gz = depth * 0.12 + frac(_Bar) * 4.0;
                        float lx = 1.0 - smoothstep(0.0, fwidth(gx) * 1.5, abs(frac(gx + 0.5) - 0.5));
                        float lz = 1.0 - smoothstep(0.0, fwidth(gz) * 1.5, abs(frac(gz + 0.5) - 0.5));
                        col = float3(0.05, 0.01, 0.10) + float3(1.0, 0.35, 0.78) * max(lx, lz) * saturate((horizon - uv.y) * 6.0) * (0.6 + 0.4 * _Beat) * k;
                    }
                }
                else if (style == 7) // Skyline: a city at night, windows changing on the beat
                {
                    float bh = 0.10 + 0.20 * hash(float2(floor(p.x * 14.0), 7.0));
                    if (uv.y < bh)
                    {
                        col = float3(0.02, 0.025, 0.06);
                        float2 g = float2(p.x * 110.0, uv.y * 80.0);
                        float2 cell = floor(g);
                        float2 f = frac(g);
                        float window = step(0.3, f.x) * step(f.x, 0.75) * step(0.3, f.y) * step(f.y, 0.75) * step(uv.y, bh - 0.012);
                        float flicker = step(0.8, hash(cell + 2.0)) * floor(_Bar * 4.0) * 0.37; // a few windows change every beat
                        col += float3(1.0, 0.82, 0.48) * window * step(0.62, hash(cell + flicker)) * (0.55 + 0.35 * _Beat) * k;
                    }
                }
                else if (style == 8) // Glow: warm soft lights drifting, swelling with the groove
                {
                    for (int o = 0; o < 8; o++)
                    {
                        float fo = o;
                        float2 c = float2(hash(float2(fo, 1.0)) * _Aspect, 0.12 + 0.75 * hash(float2(fo, 2.0)));
                        c += float2(0.04 * sin(_T * 0.35 + fo * 1.7), 0.03 * cos(_T * 0.27 + fo * 2.3));
                        float r = (0.05 + 0.06 * hash(float2(fo, 3.0))) * (1.0 + 0.18 * _Beat * k);
                        float orb = exp(-dot(p - c, p - c) / (r * r));
                        float hue = fo - 4.0 * floor(fo / 4.0);
                        float3 oc = hue < 0.5 ? float3(1.0, 0.55, 0.35) : hue < 1.5 ? float3(1.0, 0.35, 0.62) : hue < 2.5 ? float3(0.75, 0.47, 1.0) : accent;
                        col += oc * orb * 0.3 * (0.7 + 0.3 * _Energy) * k;
                    }
                }
                else if (style == 9) // Smoke: slow haze drifting across
                {
                    float2 sp = float2(p.x * 1.6 - _T * 0.04, uv.y * 2.6 + _T * 0.015);
                    float f = fbm(sp + fbm(sp * 1.3 + _T * 0.03));
                    float smoke = smoothstep(0.42, 0.8, f) * (0.35 + 0.65 * saturate(1.1 - uv.y));
                    col += float3(0.55, 0.62, 0.85) * smoke * 0.32 * (0.8 + 0.2 * _Beat) * k;
                }
                else if (style == 10) // Fireflies: hills under the moon, fireflies blinking on the beat
                {
                    float h1 = 0.24 + 0.035 * sin(p.x * 2.1 + 1.0) + 0.015 * sin(p.x * 5.3);
                    float h2 = 0.13 + 0.03 * sin(p.x * 3.1 + 2.0) + 0.01 * sin(p.x * 7.7);
                    col = lerp(col, float3(0.03, 0.08, 0.10), smoothstep(h1 + 0.002, h1 - 0.002, uv.y));
                    col = lerp(col, float3(0.015, 0.04, 0.05), smoothstep(h2 + 0.002, h2 - 0.002, uv.y));
                    float beatN = floor(_Bar * 4.0);
                    for (int fl = 0; fl < 14; fl++)
                    {
                        float ff = fl;
                        float2 c = float2(hash(float2(ff, 11.0)) * _Aspect, 0.06 + 0.32 * hash(float2(ff, 12.0)));
                        c += float2(0.02 * sin(_T * 0.5 + ff), 0.015 * sin(_T * 0.7 + ff * 2.0));
                        float lit = step(0.45, hash(float2(ff, beatN))) * (0.35 + 0.65 * _Beat);
                        float dd = dot(p - c, p - c);
                        col += float3(0.92, 1.0, 0.54) * (exp(-dd / 0.000012) + 0.35 * exp(-dd / 0.0004)) * lit * k;
                    }
                }
                else if (style == 11) // Constellations: stars join up over a phrase (4 bars), then a new one
                {
                    float phrase = floor(_Bar / 4.0);
                    float beats = frac(_Bar / 4.0) * 16.0;
                    float fade = smoothstep(16.0, 14.5, beats);
                    float2 prev = float2(0.0, 0.0);
                    for (int j = 0; j < 6; j++)
                    {
                        float fj = j;
                        float2 s = float2((0.1 + 0.8 * hash(float2(phrase, fj))) * _Aspect, 0.5 + 0.42 * hash(float2(fj + 20.0, phrase)));
                        float sd = dot(p - s, p - s);
                        col += CREAM * (exp(-sd / 0.00003) + 0.4 * exp(-sd / 0.0006)) * (0.5 + 0.5 * saturate(beats - fj * 2.0)) * fade * k;
                        if (j > 0)
                        {
                            float t = saturate((beats - (fj - 1.0) * 2.0) / 2.0);
                            float ld = segment(p, prev, lerp(prev, s, t));
                            col += CREAM * exp(-ld * ld / 0.000004) * 0.5 * fade * step(0.001, t) * k;
                        }
                        prev = s;
                    }
                }
                else // Aurora: ribbons in the cover's colours
                {
                    for (int b = 0; b < 3; b++)
                    {
                        float fb = b;
                        float yc = 0.62 + 0.07 * fb + 0.05 * sin(p.x * (1.4 + fb * 0.7) + _T * 0.22 * (fb + 1.0)) + 0.015 * sin(p.x * 6.0 + _T * 0.7);
                        float band = exp(-pow((uv.y - yc) / (0.03 + 0.012 * fb), 2.0));
                        float shimmer = 0.8 + 0.2 * sin(p.x * 7.0 + _T * 0.9 + fb * 2.0);
                        float3 ribbon = b == 0 ? lerp(float3(0.31, 0.86, 0.67), accent, 0.35) : b == 1 ? accent : lerp(accent, float3(0.75, 0.47, 1.0), 0.6);
                        col += ribbon * band * shimmer * 0.45 * (0.75 + 0.25 * _Beat) * (0.6 + 0.4 * _Energy) * k;
                    }
                }

                // A shooting star for every 100-note streak
                if (_Streak < 1.0)
                {
                    float2 sdir = normalize(float2(1.0, -0.55));
                    float2 head = float2(0.12 * _Aspect, 0.96) + sdir * _Streak * 0.75;
                    col += CREAM * trail(p, head, sdir, 0.18) * (1.0 - _Streak) * k;
                }

                // Star power turns the whole sky gold
                col = lerp(col, col * float3(1.3, 1.05, 0.55) + float3(0.10, 0.06, 0.0), _Gold * 0.75);

                col = saturate(col);
                #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(col);
                #endif
                return float4(col, i.color.a);
            }
            ENDCG
        }
    }
}
