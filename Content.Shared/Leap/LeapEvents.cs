using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.Leap;

[Serializable, NetSerializable]
public sealed class LeapFinishEvent : SimpleDoAfterEvent { }
