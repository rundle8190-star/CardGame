// 한국어 조사 고르기: 앞 단어의 받침을 보고 을/를, 이/가, 은/는, 와/과, 으로/로 를 붙인다.
// "검사을(를)" 같은 표기 대신 쓴다. 순수 C# (Unity 의존 없음).
namespace CardGame.Core
{
    public static class Josa
    {
        public static string Eul(string w) { return Pick(w, "을", "를"); }
        public static string I(string w) { return Pick(w, "이", "가"); }
        public static string Eun(string w) { return Pick(w, "은", "는"); }
        public static string Gwa(string w) { return Pick(w, "과", "와"); }
        public static string A(string w) { return Pick(w, "아", "야"); }
        public static string Ieyo(string w) { return Pick(w, "이에요", "예요"); }

        // 으로/로: 받침이 없거나 ㄹ 받침이면 "로"
        public static string Ro(string w)
        {
            int j = Final(w);
            return (w ?? "") + (j == 0 || j == 8 ? "로" : "으로");
        }
        public static string Ro(int n) { return Ro(n.ToString()); }
        public static string Eul(int n) { return Eul(n.ToString()); }
        public static string I(int n) { return I(n.ToString()); }

        // withFinal: 받침이 있을 때 붙일 말, noFinal: 받침이 없을 때
        public static string Pick(string w, string withFinal, string noFinal)
        {
            return (w ?? "") + (Final(w) != 0 ? withFinal : noFinal);
        }

        // 받침 번호 (0 = 받침 없음, 8 = ㄹ). 끝의 괄호·공백·문장부호는 건너뛴다.
        public static int Final(string w)
        {
            if (string.IsNullOrEmpty(w)) return 0;
            int i = w.Length - 1;
            // "검사 (임시)" 처럼 끝이 괄호면 괄호 앞 단어를 본다
            if (w[i] == ')')
            {
                int open = w.LastIndexOf('(');
                if (open > 0) i = open - 1;
            }
            while (i >= 0 && (w[i] == ' ' || w[i] == '.' || w[i] == '!' || w[i] == '?' || w[i] == '"' || w[i] == '\'')) i--;
            if (i < 0) return 0;
            char c = w[i];
            if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28;
            if (c >= '0' && c <= '9')
            {
                // 영(0) 일(1) 이(2) 삼(3) 사(4) 오(5) 육(6) 칠(7) 팔(8) 구(9). 10, 100 등은 십·백(받침 있음)
                if (c == '0' && i > 0 && char.IsDigit(w[i - 1])) return 1;
                switch (c)
                {
                    case '1': case '7': case '8': return 8;   // ㄹ 받침
                    case '0': case '3': case '6': return 1;
                    default: return 0;
                }
            }
            // 영어 등: 자음으로 끝나면 받침이 있다고 본다 (대략)
            char l = char.ToLowerInvariant(c);
            if (l == 'l') return 8;
            if (l >= 'a' && l <= 'z') return "aeiouy".IndexOf(l) >= 0 ? 0 : 1;
            return 0;
        }
    }
}
