using System;

namespace CLib {
    public class ArmaRequest {
        public int TaskId { get; private set; }
        public string ExtensionName { get; private set; }
        public string ActionName { get; private set; }
        public string Data { get; private set; }

        public static ArmaRequest Parse(string input) {
            var headerStart = input.IndexOf(ControlCharacter.SOH);
            var textStart = input.IndexOf(ControlCharacter.STX);
            var textEnd = input.IndexOf(ControlCharacter.ETX);

            var header = input.Substring(headerStart < 0 ? 0 : headerStart + 1, (textStart < 0 ? input.Length : textStart) - 1);
            var headerValues = header.Split(new [] { ControlCharacter.US }, 3);

            var request = new ArmaRequest();
            if (!int.TryParse(headerValues[0], out var taskId))
                throw new ArgumentException($"Invalid task id: {headerValues[0]}");
            request.TaskId = taskId;
            request.ExtensionName = headerValues[1].Trim();
            request.ActionName = headerValues[2].Trim();
            request.Data = textStart < 0 ? "" : input.Substring(textStart + 1, textEnd - textStart - 1);

            return request;
        }
    }
}