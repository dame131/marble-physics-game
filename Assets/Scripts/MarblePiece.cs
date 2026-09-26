using UnityEngine;

public sealed class MarblePiece : MonoBehaviour
{
    public int ColorIndex, Row = -1, Column = -1;
    public bool IsFalling, Scored;
    public Rigidbody Body;
}
