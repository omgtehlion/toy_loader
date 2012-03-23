namespace loader
{
    // Poly  : 0x1021    x^16 + x^12 + x^5 + 1
    // Init  : 0xFFFF
    public sealed class Crc16Ccitt
    {
        uint crc = 0xFFFFu;

        public void Update(byte b)
        {
            crc = (((crc & 0xFF) << 8) | ((crc >> 8) & 0xFF));
            crc ^= b;
            crc ^= (crc & 0xFF) >> 4;
            crc ^= crc << 12;
            crc ^= (crc & 0xFF) << 5;
        }

        public ushort Value { get { return (ushort)crc; } }
    }
}
