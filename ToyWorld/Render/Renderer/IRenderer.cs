﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GoodAI.ToyWorld.Control;
using OpenTK;
using OpenTK.Graphics;

namespace Render.Renderer
{
    public interface IRenderer : IDisposable
    {
        INativeWindow Window { get; }
        IGraphicsContext Context { get; }

        // Allow only local creation of windows
        void CreateWindow(string title, int width, int height);
        // Allow only local creation of contexts
        void CreateContext();

        void Init();
        void Reset();

        void EnqueueRequest(IRenderRequest request);
        void EnqueueRequest(IAvatarRenderRequest request);
        void ProcessRequests(); // Each message is a render pass in general...
    }
}
