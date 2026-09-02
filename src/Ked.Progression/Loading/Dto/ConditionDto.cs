namespace Ked.Progression
{
    public sealed class ConditionDto
    {
        public string Kind { get; set; }
        public string Key { get; set; }
        public string Op { get; set; }

        // 저작 쪽은 '0'을 키 자체를 생략해서 보냄.
        // 'int?'로 두면 "없음"과 "0"이 갈려서
        // "flag == false", (= Equal 0)이 어긋남
        public int IntValue { get; set; }
    }
}