﻿using System;

namespace GoodAI.ToyWorld.Control
{
    /// <summary>
    /// 
    /// </summary>
    public interface IGameController : IDisposable
    {
        /// <summary>
        /// 
        /// </summary>
        void Init(GameSetup setup);

        /// <summary>
        /// 
        /// </summary>
        void Reset();

        /// <summary>
        /// 
        /// </summary>
        void MakeStep();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="avatarID"></param>
        T RegisterAgentRenderRequest<T>(int avatarID)
            where T : class, IAvatarRenderRequest;

        /// <summary>
        /// 
        /// </summary>
        T RegisterRenderRequest<T>()
            where T : class, IRenderRequest;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="avatarId"></param>
        /// <returns></returns>
        IAvatarController GetAvatarController(int avatarId);
    }
}
