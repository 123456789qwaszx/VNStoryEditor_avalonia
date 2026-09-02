namespace Ked.Progression
{
    public sealed class StatChangeDto
    {
        public string Key { get; set; }

        public int Amount { get; set; }

        // "Add", "Set"
        // 비어 있음 = Add
        // 챕터 JSON이 한 글자도 안 바뀌고 그대로 실려야 함.
        public string Op { get; set; }
    }
}