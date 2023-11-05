namespace Pcg.Core;
/// <summary>
    /// see https://www.gdcvault.com/play/1021848/Building-a-Better-Centaur-AI and https://www.gdcvault.com/play/1018040/Architecture-Tricks-Managing-Behaviors-in
    /// </summary>
    [Serializable]
    public struct CurveParametric
    {
        public enum CurveType
        {
            None,
            Linear,
            Exp,
            // Sine,
            // Cosine,
            Logistic,
            Logit,
            // Smoothstep,
            // Smootherstep,

        }

        public CurveType Type;
        public float M,K,B,C;

        public CurveParametric(CurveType type, float m, float k, float b, float c)
        {
            Type = type;
            M = m;
            K = k;
            B = b;
            C = c;
        }

        // public static void UsesAAndB(CurveType type, out bool usesA, out bool usesB)
        // {
        //     switch (type)
        //     {
        //         case CurveType.Linear:
        //         case CurveType.Exp:
        //         case CurveType.Sine:
        //         case CurveType.Cosine:
        //         case CurveType.Logit:
        //         case CurveType.Logistic:
        //         case CurveType.None:
        //         case CurveType.Smoothstep:
        //         case CurveType.Smootherstep:
        //             usesA = usesB = true;
        //             break;
        //             // usesA = true;
        //             // usesB = false;
        //             // break;
        //             // usesA = usesB = false;
        //             // break;
        //         default:
        //             throw new ArgumentOutOfRangeException(nameof(type), type, null);
        //     }
        // }

        public float Evaluate(float x)
        {
            switch (Type)
            {
                case CurveType.Linear:
                    // M slope
                    // K exponent
                    // b y-intercept (vertical shift)
                    // c x-intercept (horizontal shift)
                    return (float)Math.Clamp(M * Math.Pow(x - C, K) + B, 0, 1);
                case CurveType.Exp:
                    return (float)(1f - ((1 - Math.Pow(x, M))/1) +B);
                // case CurveType.Sine:
                // return math.sin(x * math.PI * A) + B;
                // case CurveType.Cosine:
                // return 1-math.cos(x * math.PI * A) + B;
                case CurveType.Logistic:
                    // M slope at inflection point
                    // K vertical size of the curve
                    // b y-intercept (vertical shift)
                    // c x-intercept of the inflection point (horizontal shift)
                    return (float)Math.Clamp(K/(1 +Math.Pow(1000*Math.E*M, -1*x+C) ) + B, 0, 1);
                case CurveType.Logit:
                    return  (float)(B + (Math.Log(x / (1-x))/Math.Log(M) + 2 * Math.E) / (4 * Math.E));
                // case CurveType.Smoothstep:
                // {
                // var xb01 = math.clamp(x+B, 0, 1);
                // return xb01 * xb01 * (3 - 2 * xb01);
                // }
                // case CurveType.Smootherstep:
                // {
                // var xb01 = math.clamp(x+B, 0, 1);
                // return xb01 * xb01 * math.abs(xb01) * (math.abs(xb01) * (6 * x - 15) + 10);
                // }
                default:
                    throw new ArgumentOutOfRangeException(nameof(Type), Type, "Cannot evaluate");
            }
        }
    }