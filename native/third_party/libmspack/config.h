#pragma once

#define HAVE_INTTYPES_H 1
#define SIZEOF_OFF_T 8

#ifndef _OFF_T_DEFINED
#define _OFF_T_DEFINED
typedef __int64 _off_t;
#endif

#ifndef CONSOLE2LCE_LIBMSPACK_OFF_T_DEFINED
#define CONSOLE2LCE_LIBMSPACK_OFF_T_DEFINED
typedef __int64 off_t;
#endif
