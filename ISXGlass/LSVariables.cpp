#include "LSVariables.h"
#include "Logger.h"

// ----------------------------------------------------------------------------
// Private helper
// ----------------------------------------------------------------------------

// Execute a DataParse query and return the result. Returns false if the
// query fails or the result is null.
bool LSVariables::GetElement(const char* query, LSOBJECT& result)
{
    bool parseResult = pISInterface->DataParse(query, result);
    if ((!parseResult) || (result.Type == nullptr))
    {
        Logger::Instance().Write("LSVariables::GetElement: DataParse failed for '%s'.", query);
        return false;
    }
    return true;
}

// ----------------------------------------------------------------------------
// Declare / Existence
// ----------------------------------------------------------------------------

// Declare a LavishScript variable in the global environment if it does not
// already exist. Duplicate declarations are silently ignored.
void LSVariables::Declare(const char* name, const char* type)
{
    if (Exists(name))
    {
        Logger::Instance().Write("LSVariables::Declare: %s already exists, skipping.", name);
        return;
    }
    char command[256];
    snprintf(command, sizeof(command), "DeclareVariable %s %s global", name, type);
    Logger::Instance().Write("LSVariables::Declare: %s", command);
    pISInterface->ExecuteCommand(command);
}

// Check whether a LavishScript variable exists.
bool LSVariables::Exists(const char* name)
{
    char query[256];
    snprintf(query, sizeof(query), "%s(exists)", name);
    LSOBJECT result;
    bool parseResult = pISInterface->DataParse(query, result);
    return (parseResult && (result.Type != nullptr));
}

// Check whether a key exists in a collection.
bool LSVariables::KeyExists(const char* name, const char* key)
{
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[\"%s\"](exists)", name, key);
    LSOBJECT result;
    bool parseResult = pISInterface->DataParse(query, result);
    return (parseResult && (result.Type != nullptr));
}

// Check whether an index exists in an array.
bool LSVariables::IndexExists(const char* name, unsigned int index)
{
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[%u](exists)", name, index);
    LSOBJECT result;
    bool parseResult = pISInterface->DataParse(query, result);
    return (parseResult && (result.Type != nullptr));
}

// ----------------------------------------------------------------------------
// String setters
// ----------------------------------------------------------------------------

// Set a string value in a collection by key.
void LSVariables::SetString(const char* name, const char* key, const char* value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[\"%s\",\"%s\"]", name, key, value);
    Logger::Instance().Write("LSVariables::SetString: %s", command);
    pISInterface->ExecuteCommand(command);
}

// Set a string value in an array by index.
void LSVariables::SetStringAt(const char* name, unsigned int index, const char* value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[%u,\"%s\"]", name, index, value);
    Logger::Instance().Write("LSVariables::SetStringAt: %s", command);
    pISInterface->ExecuteCommand(command);
}

// ----------------------------------------------------------------------------
// Set specializations
// ----------------------------------------------------------------------------

template<> void LSVariables::Set<int>(const char* name, const char* key, int value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[\"%s\",%d]", name, key, value);
    Logger::Instance().Write("LSVariables::Set<int>: %s", command);
    pISInterface->ExecuteCommand(command);
}

template<> void LSVariables::Set<unsigned int>(const char* name, const char* key, unsigned int value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[\"%s\",%u]", name, key, value);
    Logger::Instance().Write("LSVariables::Set<uint>: %s", command);
    pISInterface->ExecuteCommand(command);
}

template<> void LSVariables::Set<float>(const char* name, const char* key, float value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[\"%s\",%f]", name, key, value);
    Logger::Instance().Write("LSVariables::Set<float>: %s", command);
    pISInterface->ExecuteCommand(command);
}

template<> void LSVariables::Set<unsigned char>(const char* name, const char* key, unsigned char value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[\"%s\",%u]", name, key, (unsigned int)value);
    Logger::Instance().Write("LSVariables::Set<byte>: %s", command);
    pISInterface->ExecuteCommand(command);
}

// ----------------------------------------------------------------------------
// SetAt specializations
// ----------------------------------------------------------------------------

template<> void LSVariables::SetAt<int>(const char* name, unsigned int index, int value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[%u,%d]", name, index, value);
    Logger::Instance().Write("LSVariables::SetAt<int>: %s", command);
    pISInterface->ExecuteCommand(command);
}

template<> void LSVariables::SetAt<unsigned int>(const char* name, unsigned int index, unsigned int value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[%u,%u]", name, index, value);
    Logger::Instance().Write("LSVariables::SetAt<uint>: %s", command);
    pISInterface->ExecuteCommand(command);
}

template<> void LSVariables::SetAt<float>(const char* name, unsigned int index, float value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[%u,%f]", name, index, value);
    Logger::Instance().Write("LSVariables::SetAt<float>: %s", command);
    pISInterface->ExecuteCommand(command);
}

template<> void LSVariables::SetAt<unsigned char>(const char* name, unsigned int index, unsigned char value)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Set[%u,%u]", name, index, (unsigned int)value);
    Logger::Instance().Write("LSVariables::SetAt<byte>: %s", command);
    pISInterface->ExecuteCommand(command);
}

// ----------------------------------------------------------------------------
// String getters
// ----------------------------------------------------------------------------

// Get a string value from a collection by key into caller-provided buffer.
bool LSVariables::GetString(const char* name, const char* key, char* buf, unsigned int buflen)
{
    if (!KeyExists(name, key))
    {
        Logger::Instance().Write("LSVariables::GetString: key '%s' not found in %s.", key, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[\"%s\"]", name, key);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    buf[0] = 0;
    result.Type->ToText(result.GetObjectData(), buf, buflen);
    Logger::Instance().Write("LSVariables::GetString: %s.Element[%s] = %s", name, key, buf);
    return true;
}

// Get a string value from an array by index into caller-provided buffer.
bool LSVariables::GetStringAt(const char* name, unsigned int index, char* buf, unsigned int buflen)
{
    if (!IndexExists(name, index))
    {
        Logger::Instance().Write("LSVariables::GetStringAt: index %u not found in %s.", index, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[%u]", name, index);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    buf[0] = 0;
    result.Type->ToText(result.GetObjectData(), buf, buflen);
    Logger::Instance().Write("LSVariables::GetStringAt: %s.Element[%u] = %s", name, index, buf);
    return true;
}

// ----------------------------------------------------------------------------
// Get specializations
// ----------------------------------------------------------------------------

template<> bool LSVariables::Get<int>(const char* name, const char* key, int& value)
{
    if (!KeyExists(name, key))
    {
        Logger::Instance().Write("LSVariables::Get<int>: key '%s' not found in %s.", key, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[\"%s\"]", name, key);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    value = (int)result.DWord;
    Logger::Instance().Write("LSVariables::Get<int>: %s.Element[%s] = %d", name, key, value);
    return true;
}

template<> bool LSVariables::Get<unsigned int>(const char* name, const char* key, unsigned int& value)
{
    if (!KeyExists(name, key))
    {
        Logger::Instance().Write("LSVariables::Get<uint>: key '%s' not found in %s.", key, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[\"%s\"]", name, key);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    value = result.DWord;
    Logger::Instance().Write("LSVariables::Get<uint>: %s.Element[%s] = %u", name, key, value);
    return true;
}

template<> bool LSVariables::Get<float>(const char* name, const char* key, float& value)
{
    if (!KeyExists(name, key))
    {
        Logger::Instance().Write("LSVariables::Get<float>: key '%s' not found in %s.", key, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[\"%s\"]", name, key);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    value = result.Float;
    Logger::Instance().Write("LSVariables::Get<float>: %s.Element[%s] = %f", name, key, value);
    return true;
}

template<> bool LSVariables::Get<unsigned char>(const char* name, const char* key, unsigned char& value)
{
    if (!KeyExists(name, key))
    {
        Logger::Instance().Write("LSVariables::Get<byte>: key '%s' not found in %s.", key, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[\"%s\"]", name, key);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    value = (unsigned char)result.DWord;
    Logger::Instance().Write("LSVariables::Get<byte>: %s.Element[%s] = %u", name, key, (unsigned int)value);
    return true;
}

// ----------------------------------------------------------------------------
// GetAt specializations
// ----------------------------------------------------------------------------

template<> bool LSVariables::GetAt<int>(const char* name, unsigned int index, int& value)
{
    if (!IndexExists(name, index))
    {
        Logger::Instance().Write("LSVariables::GetAt<int>: index %u not found in %s.", index, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[%u]", name, index);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    value = (int)result.DWord;
    Logger::Instance().Write("LSVariables::GetAt<int>: %s.Element[%u] = %d", name, index, value);
    return true;
}

template<> bool LSVariables::GetAt<unsigned int>(const char* name, unsigned int index, unsigned int& value)
{
    if (!IndexExists(name, index))
    {
        Logger::Instance().Write("LSVariables::GetAt<uint>: index %u not found in %s.", index, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[%u]", name, index);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    value = result.DWord;
    Logger::Instance().Write("LSVariables::GetAt<uint>: %s.Element[%u] = %u", name, index, value);
    return true;
}

template<> bool LSVariables::GetAt<float>(const char* name, unsigned int index, float& value)
{
    if (!IndexExists(name, index))
    {
        Logger::Instance().Write("LSVariables::GetAt<float>: index %u not found in %s.", index, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[%u]", name, index);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    value = result.Float;
    Logger::Instance().Write("LSVariables::GetAt<float>: %s.Element[%u] = %f", name, index, value);
    return true;
}

template<> bool LSVariables::GetAt<unsigned char>(const char* name, unsigned int index, unsigned char& value)
{
    if (!IndexExists(name, index))
    {
        Logger::Instance().Write("LSVariables::GetAt<byte>: index %u not found in %s.", index, name);
        return false;
    }
    char query[256];
    snprintf(query, sizeof(query), "%s.Element[%u]", name, index);
    LSOBJECT result;
    if (!GetElement(query, result))
    {
        return false;
    }
    value = (unsigned char)result.DWord;
    Logger::Instance().Write("LSVariables::GetAt<byte>: %s.Element[%u] = %u", name, index, (unsigned int)value);
    return true;
}

// Remove a key from a collection.
void LSVariables::Erase(const char* name, const char* key)
{
    char command[256];
    snprintf(command, sizeof(command), "%s:Erase[\"%s\"]", name, key);
    Logger::Instance().Write("LSVariables::Erase: %s", command);
    pISInterface->ExecuteCommand(command);
}

// Sets the iterator to the first key and retrieves it.
// Returns false if the collection is empty.
bool LSVariables::FirstKey(const char* name, char* keyBuf, unsigned int buflen)
{
    char query[256];
    snprintf(query, sizeof(query), "%s.FirstKey(exists)", name);
    LSOBJECT result;
    bool parseResult = pISInterface->DataParse(query, result);
    if ((!parseResult) || (result.Type == nullptr))
    {
        return false;
    }
    return CurrentKey(name, keyBuf, buflen);
}

// Advances the iterator to the next key and retrieves it.
// Returns false if there are no more keys.
bool LSVariables::NextKey(const char* name, char* keyBuf, unsigned int buflen)
{
    char query[256];
    snprintf(query, sizeof(query), "%s.NextKey(exists)", name);
    LSOBJECT result;
    bool parseResult = pISInterface->DataParse(query, result);
    if ((!parseResult) || (result.Type == nullptr))
    {
        return false;
    }
    return CurrentKey(name, keyBuf, buflen);
}

// Retrieves the current key from the iterator.
// Returns false if the iterator is not valid.
bool LSVariables::CurrentKey(const char* name, char* keyBuf, unsigned int buflen)
{
    char query[256];
    snprintf(query, sizeof(query), "%s.CurrentKey", name);
    LSOBJECT result;
    bool parseResult = pISInterface->DataParse(query, result);
    if ((!parseResult) || (result.Type == nullptr))
    {
        Logger::Instance().Write("LSVariables::CurrentKey: DataParse failed for %s.", query);
        return false;
    }
    keyBuf[0] = 0;
    result.Type->ToText(result.GetObjectData(), keyBuf, buflen);
    Logger::Instance().Write("LSVariables::CurrentKey: %s = %s", name, keyBuf);
    return true;
}