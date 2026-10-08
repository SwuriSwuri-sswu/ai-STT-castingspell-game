using System.IO;
using System.Text;

/// <summary>
/// float(-1~1) 오디오 샘플을 PCM16 WAV 바이트(44바이트 헤더 포함)로 변환
/// sampleRate, channels에는 반드시 실제 데이터의 값(clip.frequency, clip.channels)
/// </summary>

public static class WavEncoder
{
    public static byte[] FromSamples(float[] samples, int channels, int sampleRate)
    {
        int dataBytes = samples.Length * 2; // 16비트 = 2바이트

        using (var ms = new MemoryStream(44 + dataBytes))
        using (var w = new BinaryWriter(ms)) // 리틀엔디언으로 기록 (WAV 규격과 동일)
        {
            // RIFF 헤더
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt 청크
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);                              // fmt 청크 크기
            w.Write((short)1);                        // 1 = PCM
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);       // 초당 바이트 수
            w.Write((short)(channels * 2));           // 샘플 1개(전 채널)의 바이트 수
            w.Write((short)16);                       // 비트 수

            // data 청크
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);
            for (int i = 0; i < samples.Length; i++)
            {
                float s = samples[i];
                if (s > 1f) s = 1f;
                else if (s < -1f) s = -1f;
                w.Write((short)(s * 32767f));
            }

            w.Flush();
            return ms.ToArray();
        }
    }
}