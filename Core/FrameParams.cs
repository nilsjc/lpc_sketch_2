namespace Core
{
    public sealed class FrameParams
    {
        public float[] Lpc = default!;   // förallokeras till order
        public float   Gain;
        public bool    Voiced;
        public int     Period;
    }

    public sealed class FrameRing
    {
        private readonly FrameParams[] _buf;
        private int _head, _tail, _count;

        public FrameRing(int capacity, int order)
        {
            _buf = new FrameParams[capacity];
            for (int i = 0; i < capacity; i++)
                _buf[i] = new FrameParams { Lpc = new float[order] };
        }
        public int Count => _count;

        public bool Empty => _count == 0;
        public bool Full  => _count == _buf.Length;

        public FrameParams WriteSlot => _buf[_tail];                 // fyll, sen Commit()
        public void Commit() { _tail = (_tail + 1) % _buf.Length; _count++; }
        public FrameParams Read() { var f = _buf[_head]; _head = (_head + 1) % _buf.Length; _count--; return f; }
    }
}