using ProtoBuf;

namespace canjewelry.src
{
    /// <summary>Server to client: the inspection as it stands. Opens the dialog the first time.</summary>
    [ProtoContract]
    public class GemInspectStatePacket
    {
        [ProtoMember(1)] public string SessionId;
        [ProtoMember(2)] public int Attempts;
        // Light level at the player.
        [ProtoMember(3)] public int LightLevel;
        [ProtoMember(4)] public string[] DefectTraits;
        [ProtoMember(5)] public float[] DefectPos;
        [ProtoMember(6)] public float[] DefectNormals;
        // Whole-stone traits, seen at a glance.
        [ProtoMember(7)] public string[] SeenTraits;
        // What the last click did: "", "miss", "unavailable" (the mode does not work in this light) or the trait it ground out.
        [ProtoMember(8)] public string LastResult;
        [ProtoMember(9)] public bool Finished;
        // The gem item, for the client to draw: the gem itself is held by the server meanwhile.
        [ProtoMember(10)] public string GemCode;
        // Seed of the gem's own shape.
        [ProtoMember(12)] public int ShapeSeed;
        // Game hour until which the gem is wet; the client counts down against its own clock.
        [ProtoMember(13)] public double WetUntil;
    }

    /// <summary>Client to server: a click on the preview, with the view it was made in.</summary>
    [ProtoContract]
    public class GemInspectClickPacket
    {
        [ProtoMember(1)] public string SessionId;
        [ProtoMember(2)] public float X;
        [ProtoMember(3)] public float Y;
        [ProtoMember(4)] public float RotationX;
        [ProtoMember(5)] public float RotationY;
        [ProtoMember(6)] public float GuiScale;
        // The gem's gui transform the client drew with; the server has none of its own.
        [ProtoMember(7)] public float[] Origin;
        [ProtoMember(8)] public float[] Translation;
        [ProtoMember(9)] public float[] Scale;
        [ProtoMember(10)] public float[] BaseRotation;
    }

    /// <summary>Client to server: the player is done looking.</summary>
    [ProtoContract]
    public class GemInspectClosePacket
    {
        [ProtoMember(1)] public string SessionId;
    }
}
