using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // Shared by the modules that measure their work: a result is Poor, Fair, Good or Excellent, and a planned event has a saved kind. Owner: the framework.

    public enum ResultQuality : byte { Poor, Fair, Good, Excellent }

    public static class PendingEvent
    {
        public const int None = 0, Checkups = 1, Illness = 2, Testing = 3;
    }
}
