using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Core
{
    public class FileRender
    {
        public void RenderToFile(Span<FrameParams> frames, string fileName)
        {
            // var block = new float[4096];
            // using var writer = new WaveFileWriter(fileName, waveFormat);
            // int n;
            // while ((n = Render(block, frames)) > 0)                   // = motorn
            //     writer.WriteSamples(block, 0, n);
        }
    }
}