using System;
using System.Collections.Generic;
using System.Text;

namespace Slzhly.Core.Utils.Infrastructure
{
    public abstract class Entity<T>
    {
        public T Id { get; set; }
    }
}
