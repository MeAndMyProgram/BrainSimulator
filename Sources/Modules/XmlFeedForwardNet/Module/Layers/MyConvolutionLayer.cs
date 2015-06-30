﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using GoodAI.Core.Memory;
using GoodAI.Core.Observers;
using GoodAI.Core.Utils;
using GoodAI.Core.Task;
using XmlFeedForwardNet.Networks;
using GoodAI.Core;

namespace XmlFeedForwardNet.Layers
{
    public class MyConvolutionLayer : MyAbstractWeightLayer
    {
        private MyCudaKernel m_forwardKernel;
        private MyCudaKernel m_backwardKernel;
        private MyCudaKernel m_weightKernel;
        private MyCudaKernel m_setKernel;

        public uint[][] FeatureInputs { get; protected set; }
        public CUdeviceptr FeatureInfos { get; protected set; }

        public uint XStride;
        public uint YStride;

        /* 
         * Observers not implemented
         * 
        public override MyOutputView CreateView()
        {
            throw new NotImplementedException();
            //return new MyConvolutionLayerView(m_network, this, 0xFFFFDDDD);
        }*/

        public MyConvolutionLayer(MyAbstractFeedForwardNode network, uint featuresCount, uint kernelWidth, uint kernelHeight, uint xStride = 1, uint yStride = 1,
                                    uint[][] featureInputs = null,
                                    float[] initialWeight = null, float[] initialBias = null)
            : base(network)
        {
            if (featureInputs == null)
            {
                // Full connection
                m_output.Nb = featuresCount;
            }
            else
            {
                // Selective connection
                m_output.Nb = featureInputs.Length;
                FeatureInputs = featureInputs;
            }

            m_weight.Width = kernelWidth;
            m_weight.Height = kernelHeight;
            XStride = xStride;
            YStride = yStride;

            m_initialWeight = initialWeight;
            m_initialBias = initialBias;
        }

        public override void Dimension(MyAbstractFLayer previousLayer)
        {
            base.Dimension(previousLayer);

            DimensionRoutageTable(previousLayer);

            uint Kernel2dCount = 0;
            for (uint featureMapId = 0; featureMapId < m_output.Nb; featureMapId++)
                Kernel2dCount += (uint)FeatureInputs[featureMapId].Length;
            m_weight.Nb += Kernel2dCount;
            m_weight.Depth = 1;


            m_bias.Width = 1;
            m_bias.Height = 1;
            m_bias.Nb = m_output.Nb;


            if (PreviousLayer.Output.Width < m_weight.Width)
                throw new MyFeedForwardLayerException("ConvolutionLayer: Input width is smaller than kernel width");
            if (PreviousLayer.Output.Height < m_weight.Height)
                throw new MyFeedForwardLayerException("ConvolutionLayer: Input height is smaller than kernel height");

            m_output.Width = (PreviousLayer.Output.Width - m_weight.Width) / XStride + 1;
            m_output.Height = (PreviousLayer.Output.Height - m_weight.Height) / YStride + 1;

        }

        protected void DimensionRoutageTable(MyAbstractFLayer previousLayer)
        {
            // Create or validate the map routage
            if (FeatureInputs == null) // The input is fully connected and must be autogenerated
            {
                FeatureInputs = new uint[m_output.Nb][];
                uint inputNb = PreviousLayer.Output.Nb;
                for (uint featureMapId = 0; featureMapId < m_output.Nb; featureMapId++)
                {
                    FeatureInputs[featureMapId] = new uint[inputNb];
                    for (uint inputId = 0; inputId < inputNb; inputId++)
                    {
                        FeatureInputs[featureMapId][inputId] = inputId;
                    }
                }
            }
            else // If the array is provided by the user, check the index validity
            {
                uint nbInput = PreviousLayer.Output.Nb;
                for (uint featureMapId = 0; featureMapId < m_output.Nb; featureMapId++)
                {
                    for (uint i = 0; i < FeatureInputs[featureMapId].Length; i++)
                        if (FeatureInputs[featureMapId][i] >= nbInput)
                            throw new MyFeedForwardLayerException("ConvolutionLayer: Input index " + FeatureInputs[featureMapId][i] + " out of range [0.." + (nbInput - 1) + "]");
                }
            }

            // Example of diposition
            // 
            //  ||    NbSources    ||      Offset     ||              SourceId             ||
            //  |-------------------------------------------------------------------------|
            //  ||  1  |  3  |  2  ||  0  |  1  |  4  ||  0  |  1  |  2  |  4  |  0  |  3  ||
            //  ------------------------------------------------------------------------------
            //      |     |    |       |     |     |      ^     ^                 ^
            //      |     |    |       ------|-----|------      |                 |
            //      |     |    |             ------|-------------                 |
            //      |     |    |                   --------------------------------
            //      |     |    | 
            //      |     |    |                        _____
            //       -----|----|---------------------------  _____ _____ _____
            //             ----|---------------------------------------       _____ _____
            //                  -----------------------------------------------------
            //

            // Dimension the routage info vector
            uint totalSize = (uint)FeatureInputs.Length /* Sizes */ + (uint)FeatureInputs.Length /* Offset */;
            for (uint featureMapId = 0; featureMapId < FeatureInputs.Length; featureMapId++)
                totalSize += (uint)FeatureInputs[featureMapId].Length;
            m_extraSize = totalSize;
        }

        public override void Initialize(Int32 nGPU)
        {
            // Create the kernels
            m_setKernel = MyKernelFactory.Instance.Kernel(nGPU, @"Common\SetKernel");
            m_forwardKernel = MyKernelFactory.Instance.Kernel(nGPU, @"XmlFeedForwardNet\ConvolutionLayerKernel", "ForwardKernel");
            m_backwardKernel = MyKernelFactory.Instance.Kernel(nGPU, @"XmlFeedForwardNet\ConvolutionLayerKernel", "BackwardKernel");
            m_weightKernel = MyKernelFactory.Instance.Kernel(nGPU, @"XmlFeedForwardNet\ConvolutionLayerKernel", "WeightKernel");

            base.Initialize(nGPU);

            // Routage
            uint nbMaps = (uint)FeatureInputs.Length;
            uint nbSourceOffset = m_extraOffset;
            uint featureInfoOffset = nbSourceOffset + nbMaps;
            uint sourceIndexOffset = featureInfoOffset + nbMaps;

            // Write features' number of sources
            uint extraOffset = 0;
            for (uint featureMapId = 0; featureMapId < nbMaps; featureMapId++)
            {
                uint[] featureSources = FeatureInputs[featureMapId];
                uint featureNbSources = (uint)featureSources.Length;

                // Write features' number of sources
                m_extraBlock.Host[nbSourceOffset + featureMapId] = featureNbSources;

                // Write feature info offset
                m_extraBlock.Host[featureInfoOffset + featureMapId] = extraOffset;

                for (uint inputId = 0; inputId < featureNbSources; inputId++)
                    m_extraBlock.Host[sourceIndexOffset + extraOffset + inputId] = featureSources[inputId];

                extraOffset += featureNbSources;
            }

            // Pointers
            FeatureInfos = m_extraBlock.GetDevicePtr(m_network, (int)nbSourceOffset);
        }

        public override void Forward()
        {
            m_forwardKernel.SetupExecution(Output.Count);
            m_forwardKernel.Run(FeatureInfos,
                                OutputDataPtr,
                                PreviousLayer.OutputDataPtr,
                                WeightDataPtr,
                                BiasDataPtr,
                                XStride,
                                YStride

                );
        }

        public override void BroadcastDelta()
        {
            // Set the previous layer deltas to zeros
            int previousDeltasCount = m_previousBackwardLayer.Delta.Count;
            m_setKernel.SetupExecution(previousDeltasCount);
            m_setKernel.Run(m_previousBackwardLayer.Delta.Ptr, 0, 0, previousDeltasCount);

            // Backpropagate
            m_backwardKernel.SetupExecution(Output.Count);
            m_backwardKernel.Run(FeatureInfos,
                                DeltaDataPtr,
                                m_previousBackwardLayer.DeltaDataPtr,
                                WeightDataPtr,
                                XStride,
                                YStride
                                );
        }

        public override void Backward()
        {
            // Update the batch weight
            m_weightKernel.SetupExecution(Output.Count);
            m_weightKernel.Run(FeatureInfos,
                                PreviousLayer.OutputDataPtr,
                                DeltaDataPtr,
                                WeightChangeDataPtr,
                                BiasChangeDataPtr,
                                XStride,
                                YStride
                                );
        }

        protected override void GenerateWeightFromRandom()
        {
            // Choose an appropriate StdDev
            // Trick found in The ConvNetJs project sources (file convnet_vol.js)
            // Allows to keep the same variance (=1) on every neuron
            float stdDev = (float)Math.Sqrt(1f / (int)(Weight.Size * PreviousLayer.Output.Nb));

            int synapticWeightCount = Weight.Count;

            // CUDA needs a even number of generated numbers
            if (synapticWeightCount % 2 != 0)
                synapticWeightCount += 1;

            MyKernelFactory.Instance.GetRandDevice(m_network).GenerateNormal32(Weight.Ptr, synapticWeightCount, 0, stdDev);
        }

        protected override void GenerateBiasFromRandom()
        {
            // Set the bias to positive value
            float biasInitialValue = 0.5f;
            m_setKernel.SetupExecution(m_bias.Nb);
            m_setKernel.Run(Bias.Ptr, 0, biasInitialValue, m_bias.Nb);
        }
    }
}
