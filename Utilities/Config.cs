using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using ProtoBuf;

using Sandbox.ModAPI;

using VRage.Game;
using VRage.Utils;

namespace AiEnabled.Utilities
{
  public static class Config
  {

    public static T ReadFileFromGameLocation<T>(string filename, Logger log)
    {
      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInGameContent(filename))
        {
          log?.Log($"File doesn't exist: {filename}");
          return default(T);
        }

        using (var reader = MyAPIGateway.Utilities.ReadFileInGameContent(filename))
        {
          var file = reader.ReadToEnd();
          return string.IsNullOrWhiteSpace(file) ? default(T) : MyAPIGateway.Utilities.SerializeFromXML<T>(file);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.ReadFileFromGameLocation: {e}");
        log?.LogAll($"Error reading the file '{filename}' from game content\n{e}", MessageType.ERROR);
        return default(T);
      }
    }

    public static T ReadFileFromModLocation<T>(string filename, MyObjectBuilder_Checkpoint.ModItem checkpoint, Logger log)
    {
      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInModLocation(filename, checkpoint))
        {
          log?.Log($"File doesn't exist: {filename}");
          return default(T);
        }

        using (var reader = MyAPIGateway.Utilities.ReadFileInModLocation(filename, checkpoint))
        {
          var file = reader.ReadToEnd();
          return string.IsNullOrWhiteSpace(file) ? default(T) : MyAPIGateway.Utilities.SerializeFromXML<T>(file);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.ReadFileFromModLocation: {e}");
        log?.LogAll($"Error reading the file '{filename}' from mod content\n{e}", MessageType.ERROR);
        return default(T);
      }
    }

    public static T ReadBinaryFileFromGameLocation<T>(string filename, Logger log)
    {
      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInGameContent(filename))
        {
          log?.Log($"File doesn't exist: {filename}");
          return default(T);
        }

        using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInGameContent(filename))
        {
          var file = reader.ReadBytes((int)reader.BaseStream.Length);
          return (file == null || file.Length == 0) ? default(T) : MyAPIGateway.Utilities.SerializeFromBinary<T>(file);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.ReadBinaryFileFromGameLocation: {e}");
        log?.LogAll($"Error reading the binary file '{filename}' from game content\n{e}", MessageType.ERROR);
        return default(T);
      }
    }

    public static T ReadBinaryFileFromModLocation<T>(string filename, MyObjectBuilder_Checkpoint.ModItem checkpoint, Logger log)
    {
      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInModLocation(filename, checkpoint))
        {
          log?.Log($"File doesn't exist: {filename}");
          return default(T);
        }

        using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInModLocation(filename, checkpoint))
        {
          var file = reader.ReadBytes((int)reader.BaseStream.Length);
          return (file == null || file.Length == 0) ? default(T) : MyAPIGateway.Utilities.SerializeFromBinary<T>(file);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.ReadBinaryFileFromModLocation: {e}");
        log?.LogAll($"Error reading the binary file '{filename}' from mod content\n{e}", MessageType.ERROR);
        return default(T);
      }
    }

    public static T ReadFileFromLocalStorage<T>(string filename, Type type, Logger log)
    {
      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, type))
        {
          log?.Log($"File doesn't exist: {filename}");
          return default(T);
        }

        using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(filename, type))
        {
          var file = reader.ReadToEnd();
          return string.IsNullOrWhiteSpace(file) ? default(T) : MyAPIGateway.Utilities.SerializeFromXML<T>(file);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.ReadFileFromLocalStorage: {e}");
        log?.LogAll($"Error reading the file '{filename}' from local storage\n{e}", MessageType.ERROR);
        return default(T);
      }
    }

    public static T ReadFileFromWorldStorage<T>(string filename, Type type, Logger log)
    {
      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, type))
        {
          log?.Log($"File doesn't exist: {filename}");
          return default(T);
        }

        using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, type))
        {
          var file = reader.ReadToEnd();
          return string.IsNullOrWhiteSpace(file) ? default(T) : MyAPIGateway.Utilities.SerializeFromXML<T>(file);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.ReadFileFromWorldStorage: {e}");
        log?.LogAll($"Error reading the file '{filename}' from world storage\n{e}", MessageType.ERROR);
        return default(T);
      }
    }

    public static T ReadBinaryFileFromWorldStorage<T>(string filename, Type type, Logger log)
    {
      try
      {
        if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, type))
        {
          log?.Log($"File doesn't exist: {filename}");
          return default(T);
        }

        using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInWorldStorage(filename, type))
        {
          var file = reader.ReadBytes((int)reader.BaseStream.Length);
          return (file == null || file.Length == 0) ? default(T) : MyAPIGateway.Utilities.SerializeFromBinary<T>(file);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.ReadBinaryFileFromWorldStorage: {e}");
        log?.LogAll($"Error reading binary file '{filename}' from world storage\n{e}", MessageType.ERROR);
        return default(T);
      }
    }

    public static void WriteBinaryFileToWorldStorage<T>(string filename, Type type, T data, Logger log)
    {
      try
      {
        if (MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, type))
          MyAPIGateway.Utilities.DeleteFileInWorldStorage(filename, type);

        using (var writer = MyAPIGateway.Utilities.WriteBinaryFileInWorldStorage(filename, type))
        {
          var config = MyAPIGateway.Utilities.SerializeToBinary(data);
          writer.Write(config);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.WriteBinaryFileToWorldStorage: {e}");
        log?.LogAll($"Error writing the binary file '{filename}' in world storage\n{e}", MessageType.ERROR);
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
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.WriteFileToWorldStorage: {e}");
        log?.LogAll($"Error writing the file '{filename}' in world storage\n{e}", MessageType.ERROR);
      }
    }

    public static void WriteBinaryFileToLocalStorage<T>(string filename, Type type, T data, Logger log)
    {
      try
      {
        if (MyAPIGateway.Utilities.FileExistsInLocalStorage(filename, type))
          MyAPIGateway.Utilities.DeleteFileInLocalStorage(filename, type);

        using (var writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(filename, type))
        {
          var config = MyAPIGateway.Utilities.SerializeToBinary(data);
          writer.Write(config);
        }
      }
      catch (Exception e)
      {
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.WriteBinaryFileToLocalStorage: {e}");
        log?.LogAll($"Error writing the binary file '{filename}' in local storage\n{e}", MessageType.ERROR);
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
        MyLog.Default.WriteLineAndConsole($"Error in AiEnabled.Config.WriteFileToLocalStorage: {e}");
        log?.LogAll($"Error writing the file '{filename}' in local storage\n{e}", MessageType.ERROR);
      }
    }
  }
}
