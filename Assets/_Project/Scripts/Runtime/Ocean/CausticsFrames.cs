using UnityEngine;

namespace Letsail.Ocean
{
    /// <summary>
    /// 물속 빛무늬(코스틱) 애니메이션 프레임 묶음. 처음과 끝이 이어지게 구워 두어 계속 돌려 재생하면 된다.
    /// 에디터 메뉴 Letsail > 물속 빛무늬 굽기 로 만든다.
    /// </summary>
    [CreateAssetMenu(menuName = "Letsail/Caustics Frames")]
    public sealed class CausticsFrames : ScriptableObject
    {
        [SerializeField] Texture2D[] frames = new Texture2D[0];

        public int Count => frames != null ? frames.Length : 0;

        public Texture2D this[int index] => frames[index];

        public void SetFrames(Texture2D[] value) => frames = value;
    }
}
