﻿using GoodAI.Core.Memory;
using GoodAI.Core.Task;
using GoodAI.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YAXLib;

namespace GoodAI.Core.Nodes
{    
    public class MyParentInput : MyNode
    {
        [MyOutputBlock]
        public MyMemoryBlock<float> Output
        {
            get { return GetOutput(0); }
            set { }
        }

        [YAXSerializableField, YAXAttributeForClass]
        public int ParentInputIndex { get; internal set; }        

        public override sealed MyMemoryBlock<float> GetOutput(int index)
        {
            Debug.Assert(index == 0, "ParentInput cannot have multiple outputs");
            return Parent != null ? Parent.GetInput(ParentInputIndex) : null;
        }

        public override sealed MyMemoryBlock<T> GetOutput<T>(int index)
        {
            Debug.Assert(index == 0, "ParentInput cannot have multiple outputs");
            return Parent != null ? Parent.GetInput<T>(ParentInputIndex) : null;
        }

        public override MyAbstractMemoryBlock GetAbstractOutput(int index)
        {
            Debug.Assert(index == 0, "ParentInput cannot have multiple outputs");
            return Parent != null ? Parent.GetAbstractInput(ParentInputIndex) : null;
        }

        public override int OutputBranches
        {
            get { return 1; }
            set { }
        }

        public override void UpdateMemoryBlocks() { }
        public override void Validate(MyValidator validator) { }
    }
}
