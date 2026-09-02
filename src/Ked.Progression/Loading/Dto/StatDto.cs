namespace Ked.Progression
{
    // "Type"은 "Number" 또는 "Bool" 두 종류.
    // 저작 쪽 enum은 Int지만 내보내기가 이름을 파싱 함.
    public sealed class StatDto
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string Type { get; set; }
        public int Initial { get; set; }
        public int Minimum { get; set; }
        public int Maximum { get; set; }
    }
}