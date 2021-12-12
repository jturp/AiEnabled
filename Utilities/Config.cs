using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.ModAPI;

using VRage.Utils;

namespace AiEnabled.Utilities
{
  public static class Config
  {
    public static T ReadFileFromLocalStorage<T>(string filename, Type type, Logger log)
    {
      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, type))
          return default(T);

        using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(filename, type))
        {
          var file = reader.ReadToEnd();
          return string.IsNullOrWhiteSpace(file) ? default(T) : MyAPIGateway.Utilities.SerializeFromXML<T>(file);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.ReadFileFromLocalStorage: {e.Message}\n{e.StackTrace}");
        log?.LogAll($"Error reading the file '{filename}' from local storage\n{e.Message}\n\n{e.StackTrace}", MessageType.ERROR);
        return default(T);
      }
    }

    public static T ReadFileFromWorldStorage<T>(string filename, Type type, Logger log)
    {
      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, type))
          return default(T);

        using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, type))
        {
          var file = reader.ReadToEnd();
          return string.IsNullOrWhiteSpace(file) ? default(T) : MyAPIGateway.Utilities.SerializeFromXML<T>(file);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.ReadFileFromWorldStorage: {e.Message}\n{e.StackTrace}");
        log?.LogAll($"Error reading the file '{filename}' from world storage\n{e.Message}\n\n{e.StackTrace}", MessageType.ERROR);
        return default(T);
      }
    }


    public static void WriteFileToWorldStorage<T>(string filename, Type type, T data, Logger log)
    {
      try
      {
        if (MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, type))
          MyAPIGateway.Utilities.DeleteFileInWorldStorage(filename, type);

        using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, type))
        {
          var config = MyAPIGateway.Utilities.SerializeToXML(data);
          writer.Write(config);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.WriteFileToWorldStorage: {e.Message}\n{e.StackTrace}");
        log?.LogAll($"Error writing the file '{filename}' in world storage\n{e.Message}\n\n{e.StackTrace}", MessageType.ERROR);
      }
    }

    public static void WriteFileToLocalStorage<T>(string filename, Type type, T data, Logger log)
    {
      try
      {
        if (MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, type))
          MyAPIGateway.Utilities.DeleteFileInLocalStorage(filename, type);

        using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(filename, type))
        {
          var config = MyAPIGateway.Utilities.SerializeToXML(data);
          writer.Write(config);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.WriteFileToLocalStorage: {e.Message}\n{e.StackTrace}");
        log?.LogAll($"Error writing the file '{filename}' in local storage\n{e.Message}\n\n{e.StackTrace}", MessageType.ERROR);
      }
    }

  }
}
