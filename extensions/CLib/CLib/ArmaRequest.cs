﻿using System;

namespace CLib
{
    public class ArmaRequest
    {
        public int TaskId { get; private set; }
        public string ExtensionName { get; private set; }
        public string ActionName { get; private set; }
        public string Data { get; private set; }
        public string Response { get; private set; }

        public static ArmaRequest Parse(string input)
        {
            int headerStart = input.IndexOf((char)ControlCharacter.SOH);
            int textStart = input.IndexOf((char)ControlCharacter.STX);
            int textEnd = input.IndexOf((char)ControlCharacter.ETX);

            string header = input.Substring(headerStart < 0 ? 0 : headerStart + 1, textStart < 0 ? input.Length : textStart);
            string[] headerValues = header.Split(new char[] { (char)ControlCharacter.US }, 3);

            ArmaRequest request = new ArmaRequest();
            int taskId;
            if (!int.TryParse(headerValues[0], out taskId))
                throw new ArgumentException("Invalid task id: " + headerValues[0]);
            request.TaskId = taskId;
            request.ExtensionName = headerValues[1];
            request.ActionName = headerValues[2];
            request.Data = textStart < 0 ? "" : input.Substring(textStart + 1, textEnd - textStart - 2);

            return request;
        }
    }
}