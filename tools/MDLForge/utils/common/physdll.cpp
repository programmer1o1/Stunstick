//========= Copyright © 1996-2005, Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
// $NoKeywords: $
//
//=============================================================================//
#include <stdio.h>
#ifdef _LINUX
#include <dlfcn.h>
#include <stdlib.h>
#include <string.h>
#endif
#include "physdll.h"
#include "filesystem_tools.h"
#include "tier0/platform.h"
#include "tier1/interface.h"
#include "tier1/strtools.h"

static CSysModule *pPhysicsModule = NULL;

static const char *GetVPhysicsModuleFilename()
{
#ifdef _WIN32
	return "vphysics.dll";
#elif defined(OSX)
	return "vphysics.dylib";
#else
	return "vphysics.so";
#endif
}

static bool TryLoadPhysicsModule( const char *pModulePath )
{
#ifdef _LINUX
	int flags = RTLD_NOW;
#ifdef RTLD_DEEPBIND
	flags |= RTLD_DEEPBIND;
#endif
	pPhysicsModule = reinterpret_cast< CSysModule * >( dlopen( pModulePath, flags ) );
	return pPhysicsModule != NULL;
#else
	pPhysicsModule = Sys_LoadModule( pModulePath );
	return pPhysicsModule != NULL;
#endif
}

#ifdef _LINUX
static void PrependToLdLibraryPath( const char *pDirectory )
{
	if ( !pDirectory || !pDirectory[0] )
		return;

	const char *existing = getenv( "LD_LIBRARY_PATH" );
	if ( existing && existing[0] )
	{
		const char *start = existing;
		while ( start && *start )
		{
			const char *end = strchr( start, ':' );
			size_t segLen = end ? (size_t)( end - start ) : strlen( start );

			if ( segLen == strlen( pDirectory ) && strncmp( start, pDirectory, segLen ) == 0 )
				return;

			if ( !end )
				break;

			start = end + 1;
		}
	}

	char buffer[4096];
	if ( existing && existing[0] )
		Q_snprintf( buffer, sizeof( buffer ), "%s:%s", pDirectory, existing );
	else
		Q_snprintf( buffer, sizeof( buffer ), "%s", pDirectory );

	setenv( "LD_LIBRARY_PATH", buffer, 1 );
}

static void PrependCandidateDirectoryToLdLibraryPath( const char *pModulePath )
{
	if ( !pModulePath || !pModulePath[0] )
		return;

	char dir[1024];
	Q_strncpy( dir, pModulePath, sizeof( dir ) );
	Q_StripFilename( dir );
	Q_StripTrailingSlash( dir );
	if ( dir[0] )
	{
		PrependToLdLibraryPath( dir );
	}
}
#endif

static CSysModule *LoadPhysicsModule()
{
	if ( pPhysicsModule )
		return pPhysicsModule;

	const char *pModuleName = GetVPhysicsModuleFilename();
	if ( TryLoadPhysicsModule( pModuleName ) )
		return pPhysicsModule;

	// For most shipped Source games, the engine DLLs live in <game root>/bin,
	// while -game points at the mod directory (e.g. "<game root>/hl2").
	if ( gamedir[0] )
	{
		char modDir[1024];
		Q_strncpy( modDir, gamedir, sizeof( modDir ) );
		Q_StripTrailingSlash( modDir );

		char candidate[1024];

		// Some mods ship their own bin directory.
		Q_snprintf( candidate, sizeof( candidate ), "%s/%s/%s", modDir, PLATFORM_BIN_DIR, pModuleName );
		Q_FixSlashes( candidate );
#ifdef _LINUX
		PrependCandidateDirectoryToLdLibraryPath( candidate );
#endif
		if ( TryLoadPhysicsModule( candidate ) )
			return pPhysicsModule;

		Q_snprintf( candidate, sizeof( candidate ), "%s/bin/%s", modDir, pModuleName );
		Q_FixSlashes( candidate );
#ifdef _LINUX
		PrependCandidateDirectoryToLdLibraryPath( candidate );
#endif
		if ( TryLoadPhysicsModule( candidate ) )
			return pPhysicsModule;

		// Try parent/bin (e.g. "<game root>/bin").
		char gameRoot[1024];
		Q_strncpy( gameRoot, modDir, sizeof( gameRoot ) );
		if ( Q_StripLastDir( gameRoot, sizeof( gameRoot ) ) )
		{
			Q_StripTrailingSlash( gameRoot );
			Q_snprintf( candidate, sizeof( candidate ), "%s/%s/%s", gameRoot, PLATFORM_BIN_DIR, pModuleName );
			Q_FixSlashes( candidate );
#ifdef _LINUX
			PrependCandidateDirectoryToLdLibraryPath( candidate );
#endif
			if ( TryLoadPhysicsModule( candidate ) )
				return pPhysicsModule;

			Q_snprintf( candidate, sizeof( candidate ), "%s/bin/%s", gameRoot, pModuleName );
			Q_FixSlashes( candidate );
#ifdef _LINUX
			PrependCandidateDirectoryToLdLibraryPath( candidate );
#endif
			if ( TryLoadPhysicsModule( candidate ) )
				return pPhysicsModule;
		}
	}

	return NULL;
}

CreateInterfaceFn GetPhysicsFactory( void )
{
	if ( !LoadPhysicsModule() )
		return NULL;

	return Sys_GetFactory( pPhysicsModule );
}

void PhysicsDLLPath( const char *pPathname )
{
        if ( !pPhysicsModule )
        {
				const char *pModulePath = pPathname;
#ifndef _WIN32
				// Many legacy toolchains pass "VPHYSICS.DLL". Normalize to the platform module name
				// to avoid Sys_LoadLibrary() extension mangling (e.g. "_i486.so" suffixes).
				if ( pModulePath && !Q_stricmp( pModulePath, "VPHYSICS.DLL" ) )
				{
					pModulePath = GetVPhysicsModuleFilename();
				}
#endif

				TryLoadPhysicsModule( pModulePath );
				if ( !pPhysicsModule && pPathname && !Q_IsAbsolutePath( pPathname ) )
				{
					// Allow fallback search for bare filenames.
					LoadPhysicsModule();
				}
        }
}
