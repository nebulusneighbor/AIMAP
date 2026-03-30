using System.Collections.Generic;
using UnityEngine;

namespace AIMAP.Avatars
{
    public sealed class AvatarVariantLibrary : MonoBehaviour
    {
        [SerializeField] private List<AvatarSkinVariant> skinVariants = new List<AvatarSkinVariant>();

        public List<AvatarSkinVariant> SkinVariants => skinVariants;
    }
}
