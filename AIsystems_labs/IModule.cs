using System;
using System.Collections.Generic;
using System.Linq;

namespace AIsystems_labs
{
    public interface IModule
    {
        void Start();
        void Stop();
        string Title { get; }
    }
}