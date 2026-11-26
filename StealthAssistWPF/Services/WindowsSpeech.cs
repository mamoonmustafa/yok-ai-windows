using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using System.Threading;

namespace StealthAssistWPF.Services
{
    /// <summary>
    /// Free, offline speech to text using the recognizer built into Windows (System.Speech / SAPI).
    /// No account, no key, no internet. Audio never leaves the PC.
    /// Feed it 16 kHz, 16-bit, mono PCM with Write().
    /// </summary>
    public sealed class WindowsSpeech : IDisposable
    {
        private SpeechRecognitionEngine engine;
        private readonly FeedStream feed = new FeedStream();

        /// <summary>Text while the person is still speaking (may change).</summary>
        public event Action<string> Partial;
        /// <summary>Final text of a finished phrase.</summary>
        public event Action<string> Final;
        public event Action<string> Error;

        public string RecognizerName { get; private set; }

        /// <summary>Returns null on success, otherwise a plain explanation of what is missing.</summary>
        public string Start(string language = "en-US")
        {
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            if (installed.Count == 0)
            {
                return "Windows has no speech recognizer installed. Open Settings > Time & language > Speech, add an English speech pack, then try again (or use Azure in Settings).";
            }

            var culture = new CultureInfo(language);
            var info = installed.FirstOrDefault(r => r.Culture.Equals(culture))
                       ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
                       ?? installed[0];
            RecognizerName = info.Name;

            engine = new SpeechRecognitionEngine(info);
            engine.LoadGrammar(new DictationGrammar());
            // Shorter pauses end a phrase sooner, so answers appear faster.
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(600);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(800);

            engine.SpeechHypothesized += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Result?.Text)) Partial?.Invoke(e.Result.Text); };
            engine.SpeechRecognized += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Result?.Text)) Final?.Invoke(e.Result.Text); };
            engine.RecognizeCompleted += (s, e) => { if (e.Error != null) Error?.Invoke(e.Error.Message); };

            engine.SetInputToAudioStream(feed, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            engine.RecognizeAsync(RecognizeMode.Multiple);
            return null;
        }

        public void Write(byte[] pcm16kMono)
        {
            if (pcm16kMono != null && pcm16kMono.Length > 0) feed.Push(pcm16kMono);
        }

        public void Dispose()
        {
            try { feed.Close(); } catch { }
            try { engine?.RecognizeAsyncCancel(); } catch { }
            try { engine?.Dispose(); } catch { }
            engine = null;
        }

        /// <summary>
        /// A read-only stream that waits for audio pushed from the capture thread.
        /// System.Speech reads it on its own thread; Read blocks until data arrives or the stream closes.
        /// </summary>
        private sealed class FeedStream : Stream
        {
            private readonly BlockingCollection<byte[]> queue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());
            private byte[] current;
            private int offset;
            private long position;

            public void Push(byte[] data)
            {
                if (!queue.IsAddingCompleted)
                {
                    try { queue.Add(data); } catch (InvalidOperationException) { }
                }
            }

            // SAPI treats a short read as the end of the audio, so fill the whole request (waiting for audio if needed).
            public override int Read(byte[] buffer, int off, int count)
            {
                int total = 0;
                while (total < count)
                {
                    if (current == null || offset >= current.Length)
                    {
                        try
                        {
                            if (!queue.TryTake(out current, Timeout.Infinite)) break;
                        }
                        catch (InvalidOperationException) { break; } // closed
                        offset = 0;
                    }
                    int n = Math.Min(count - total, current.Length - offset);
                    Buffer.BlockCopy(current, offset, buffer, off + total, n);
                    offset += n;
                    total += n;
                }
                position += total;
                return total;
            }

            public override void Close()
            {
                queue.CompleteAdding();
                base.Close();
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => long.MaxValue;
            // System.Speech probes Position/Seek on its input; a live feed can't seek, so these are harmless no-ops.
            public override long Position { get => position; set { } }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin origin) => position;
            public override void SetLength(long value) { }
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }
    }
}
