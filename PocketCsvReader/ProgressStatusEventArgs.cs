using System;

namespace PocketCsvReader
{
    public class ProgressStatusEventArgs : EventArgs
    {
        public string Status { get; set; }
        public ProgressInfo Progress { get; set; }

        public ProgressStatusEventArgs(string status)
        {
            Status = status;
        }

        public ProgressStatusEventArgs(string status, int current, int total) : this(status)
        {
            Status = status;
            Progress = new ProgressInfo { Current = current, Total = total };
        }

        public struct ProgressInfo : IEquatable<ProgressInfo>
        {
            public int Current { get; internal set; }
            public int Total { get; internal set; }

            public bool Equals(ProgressInfo other)
                => Current == other.Current && Total == other.Total;
        }
    }
}
