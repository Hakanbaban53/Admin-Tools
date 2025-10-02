using System;
using System.ComponentModel;

namespace FTP_Tool.Models
{
    // Represents a daily time interval like "08:00-17:00" with simple validation
    public class IntervalItem : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _start = "00:00";
        private string _end = "00:00";

        public string Start
        {
            get => _start;
            set
            {
                if (_start == value) return;
                _start = value;
                OnPropertyChanged(nameof(Start));
                OnPropertyChanged(nameof(IntervalString));
            }
        }

        public string End
        {
            get => _end;
            set
            {
                if (_end == value) return;
                _end = value;
                OnPropertyChanged(nameof(End));
                OnPropertyChanged(nameof(IntervalString));
            }
        }

        public string IntervalString => $"{Start}-{End}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // IDataErrorInfo validation: ensure Start and End parse to TimeSpan in HH:mm format
        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                try
                {
                    if (columnName == nameof(Start))
                    {
                        if (!TimeSpan.TryParse(Start, out _)) return "Invalid time (use HH:mm)";
                    }
                    else if (columnName == nameof(End))
                    {
                        if (!TimeSpan.TryParse(End, out _)) return "Invalid time (use HH:mm)";
                    }
                }
                catch { }
                return string.Empty;
            }
        }

        public bool IsValid()
        {
            return string.IsNullOrEmpty(this[nameof(Start)]) && string.IsNullOrEmpty(this[nameof(End)]);
        }

        public static IntervalItem ParseFromString(string s)
        {
            var it = new IntervalItem();
            if (string.IsNullOrWhiteSpace(s)) return it;
            var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1) it.Start = parts[0].Trim();
            if (parts.Length >= 2) it.End = parts[1].Trim();
            return it;
        }
    }
}
