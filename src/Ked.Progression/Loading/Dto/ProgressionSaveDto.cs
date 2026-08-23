using System.Collections.Generic;

namespace Ked.Progression.Dto
{
    /// <summary>
    /// 세이브의 <b>진행 블록</b>. 한 판이 어디까지 왔나.
    ///
    /// <b>이 패키지가 소유하는 것은 이 모양까지다.</b> 파일로 굽는 법·슬롯 구조·썸네일은
    /// 호스트의 사정이고, 여기는 어트리뷰트도 직렬화기도 갖지 않는다(규율 2).
    ///
    /// ⚠ <b>대사 위치는 여기 없다.</b> <c>lineId</c>·선택 재현 기록은 대사 층의 것이라
    /// 호스트가 자기 블록에 담는다 — 같은 파일에 나란히 두더라도 주인은 각자다.
    /// 마찬가지로 <b>잠김·표시·도달 가능 집합도 없다</b>: 그건 매번 다시 계산하는 해석이지
    /// 상태가 아니다(P3).
    /// </summary>
    public sealed class ProgressionSaveDto
    {
        /// <summary>
        /// <see cref="ProgressionSave.CurrentSchemaVersion"/>과 대조된다.
        ///
        /// <b>처음부터 넣는다</b>(D4) — 나중에 넣으면 버전 없는 세이브를 영원히 특별
        /// 취급해야 한다. 올리는 것은 <b>필드가 사라지거나 뜻이 바뀔 때만</b>이고,
        /// 추가는 올리지 않는다(없는 값은 정의의 초기값이 메운다).
        /// </summary>
        public int SchemaVersion { get; set; }

        public string ScenarioId { get; set; }
        public string CurrentChapterId { get; set; }
        public string CurrentEpisodeId { get; set; }

        public Dictionary<string, int> Stats { get; set; }
        public List<string> ClearedEpisodeIds { get; set; }
        public List<string> ClearedChapterIds { get; set; }
        public List<ChapterEndingDto> EndingHistory { get; set; }
    }

    public sealed class ChapterEndingDto
    {
        public string ChapterId { get; set; }
        public string EndingKey { get; set; }
    }
}
