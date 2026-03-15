#pragma once
#include "ISXGlass.h"

// LSVariables provides a typed C++ interface for reading and writing
// LavishScript variables (collections and arrays) via ExecuteCommand
// and DataParse. Variables are declared in the global LavishScript
// environment and persist across extension reloads.

class LSVariables
{
public:
    // Declare a variable in the global LavishScript environment.
    // No-op if the variable already exists.
    // type examples: "collection:string", "collection:int", "array:float", "int"
    static void Declare(const char* name, const char* type);

    // Existence checks
    static bool Exists(const char* name);
    static bool KeyExists(const char* name, const char* key);
    static bool IndexExists(const char* name, unsigned int index);

    // Collection setters — keyed by string key
    template<typename T>
    static void Set(const char* name, const char* key, T value);
    static void SetString(const char* name, const char* key, const char* value);

    // Array setters — keyed by index
    template<typename T>
    static void SetAt(const char* name, unsigned int index, T value);
    static void SetStringAt(const char* name, unsigned int index, const char* value);

    // Collection getters — returns false if key does not exist
    template<typename T>
    static bool Get(const char* name, const char* key, T& value);
    static bool GetString(const char* name, const char* key, char* buf, unsigned int buflen);

    // Array getters — returns false if index does not exist
    template<typename T>
    static bool GetAt(const char* name, unsigned int index, T& value);
    static bool GetStringAt(const char* name, unsigned int index, char* buf, unsigned int buflen);

    // Remove a key from a collection
    static void Erase(const char* name, const char* key);

    // Iterate all keys in a collection. Returns false when no more keys exist.
    // Usage: call FirstKey to start, then NextKey to advance.
    static bool FirstKey(const char* name, char* keyBuf, unsigned int buflen);
    static bool NextKey(const char* name, char* keyBuf, unsigned int buflen);
    static bool CurrentKey(const char* name, char* keyBuf, unsigned int buflen);

private:
    static bool GetElement(const char* query, LSOBJECT& result);
};

// ----------------------------------------------------------------------------
// Set specializations
// ----------------------------------------------------------------------------

template<> void LSVariables::Set<int>(const char* name, const char* key, int value);
template<> void LSVariables::Set<unsigned int>(const char* name, const char* key, unsigned int value);
template<> void LSVariables::Set<float>(const char* name, const char* key, float value);
template<> void LSVariables::Set<unsigned char>(const char* name, const char* key, unsigned char value);

// ----------------------------------------------------------------------------
// SetAt specializations
// ----------------------------------------------------------------------------

template<> void LSVariables::SetAt<int>(const char* name, unsigned int index, int value);
template<> void LSVariables::SetAt<unsigned int>(const char* name, unsigned int index, unsigned int value);
template<> void LSVariables::SetAt<float>(const char* name, unsigned int index, float value);
template<> void LSVariables::SetAt<unsigned char>(const char* name, unsigned int index, unsigned char value);

// ----------------------------------------------------------------------------
// Get specializations
// ----------------------------------------------------------------------------

template<> bool LSVariables::Get<int>(const char* name, const char* key, int& value);
template<> bool LSVariables::Get<unsigned int>(const char* name, const char* key, unsigned int& value);
template<> bool LSVariables::Get<float>(const char* name, const char* key, float& value);
template<> bool LSVariables::Get<unsigned char>(const char* name, const char* key, unsigned char& value);

// ----------------------------------------------------------------------------
// GetAt specializations
// ----------------------------------------------------------------------------

template<> bool LSVariables::GetAt<int>(const char* name, unsigned int index, int& value);
template<> bool LSVariables::GetAt<unsigned int>(const char* name, unsigned int index, unsigned int& value);
template<> bool LSVariables::GetAt<float>(const char* name, unsigned int index, float& value);
template<> bool LSVariables::GetAt<unsigned char>(const char* name, unsigned int index, unsigned char& value);