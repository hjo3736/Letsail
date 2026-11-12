using System;
using Letsail.Player;
using Unity.Netcode;

namespace Letsail.Networking
{
    /// <summary>
    /// 네트워크로 보내는 복장. 부위마다 CharacterOutfit 의 부품 번호(0 = 없음) 하나씩, 모두 7바이트.
    /// 모든 PC가 같은 모델을 쓰므로 번호만으로 같은 부품을 가리킨다.
    /// </summary>
    public struct NetworkOutfit : INetworkSerializable, IEquatable<NetworkOutfit>
    {
        public byte Hair, Hat, Face, Top, Bottom, Shoes, Back;

        /// <summary>아직 아무것도 정해지지 않은 상태 (모든 부위 0).</summary>
        public bool IsEmpty => Hair == 0 && Hat == 0 && Face == 0 && Top == 0 && Bottom == 0 && Shoes == 0 && Back == 0;

        public static NetworkOutfit From(CharacterOutfit outfit)
        {
            return new NetworkOutfit
            {
                Hair = (byte)outfit.GetIndex(CharacterOutfit.Slot.Hair),
                Hat = (byte)outfit.GetIndex(CharacterOutfit.Slot.Hat),
                Face = (byte)outfit.GetIndex(CharacterOutfit.Slot.Face),
                Top = (byte)outfit.GetIndex(CharacterOutfit.Slot.Top),
                Bottom = (byte)outfit.GetIndex(CharacterOutfit.Slot.Bottom),
                Shoes = (byte)outfit.GetIndex(CharacterOutfit.Slot.Shoes),
                Back = (byte)outfit.GetIndex(CharacterOutfit.Slot.Back),
            };
        }

        public void ApplyTo(CharacterOutfit outfit)
        {
            outfit.SetIndex(CharacterOutfit.Slot.Hair, Hair);
            outfit.SetIndex(CharacterOutfit.Slot.Hat, Hat);
            outfit.SetIndex(CharacterOutfit.Slot.Face, Face);
            outfit.SetIndex(CharacterOutfit.Slot.Top, Top);
            outfit.SetIndex(CharacterOutfit.Slot.Bottom, Bottom);
            outfit.SetIndex(CharacterOutfit.Slot.Shoes, Shoes);
            outfit.SetIndex(CharacterOutfit.Slot.Back, Back);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Hair);
            serializer.SerializeValue(ref Hat);
            serializer.SerializeValue(ref Face);
            serializer.SerializeValue(ref Top);
            serializer.SerializeValue(ref Bottom);
            serializer.SerializeValue(ref Shoes);
            serializer.SerializeValue(ref Back);
        }

        public bool Equals(NetworkOutfit other)
        {
            return Hair == other.Hair && Hat == other.Hat && Face == other.Face && Top == other.Top
                && Bottom == other.Bottom && Shoes == other.Shoes && Back == other.Back;
        }

        public override bool Equals(object obj) => obj is NetworkOutfit other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Hair, Hat, Face, Top, Bottom, Shoes, Back);
    }
}
