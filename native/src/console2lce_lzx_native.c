#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../third_party/libmspack/system.h"
#include "../third_party/libmspack/lzx.h"

#define CONSOLE2LCE_LZX_PADDING_BYTES 16

struct memory_file {
    const uint8_t *read_buffer;
    size_t read_length;
    size_t read_position;
    uint8_t *write_buffer;
    size_t write_length;
    size_t write_position;
};

static void set_error(char *buffer, int buffer_length, const char *format, ...)
{
    va_list args;

    if ((buffer == NULL) || (buffer_length <= 0)) {
        return;
    }

    va_start(args, format);
    _vsnprintf_s(buffer, (size_t) buffer_length, _TRUNCATE, format, args);
    va_end(args);
}

static struct mspack_file *memory_open(struct mspack_system *self, const char *filename, int mode)
{
    (void) self;
    (void) filename;
    (void) mode;
    return NULL;
}

static void memory_close(struct mspack_file *file)
{
    (void) file;
}

static int memory_read(struct mspack_file *file, void *buffer, int bytes)
{
    struct memory_file *memory = (struct memory_file *) file;
    size_t available;
    size_t todo;

    if ((memory == NULL) || (buffer == NULL) || (bytes < 0)) {
        return -1;
    }

    available = memory->read_length - memory->read_position;
    todo = (size_t) bytes;
    if (todo > available) {
        todo = available;
    }

    if (todo > 0) {
        memcpy(buffer, memory->read_buffer + memory->read_position, todo);
        memory->read_position += todo;
    }

    return (int) todo;
}

static int memory_write(struct mspack_file *file, void *buffer, int bytes)
{
    struct memory_file *memory = (struct memory_file *) file;
    size_t available;
    size_t todo;

    if ((memory == NULL) || (buffer == NULL) || (bytes < 0)) {
        return -1;
    }

    available = memory->write_length - memory->write_position;
    todo = (size_t) bytes;
    if (todo > available) {
        todo = available;
    }

    if (todo > 0) {
        memcpy(memory->write_buffer + memory->write_position, buffer, todo);
        memory->write_position += todo;
    }

    return (int) todo;
}

static int memory_seek(struct mspack_file *file, off_t offset, int mode)
{
    struct memory_file *memory = (struct memory_file *) file;
    off_t adjusted = offset;

    if (memory == NULL) {
        return 1;
    }

    switch (mode) {
    case MSPACK_SYS_SEEK_START:
        break;
    case MSPACK_SYS_SEEK_CUR:
        adjusted += (off_t) memory->read_position;
        break;
    case MSPACK_SYS_SEEK_END:
        adjusted += (off_t) memory->read_length;
        break;
    default:
        return 1;
    }

    if ((adjusted < 0) || (adjusted > (off_t) memory->read_length)) {
        return 1;
    }

    memory->read_position = (size_t) adjusted;
    return 0;
}

static off_t memory_tell(struct mspack_file *file)
{
    const struct memory_file *memory = (const struct memory_file *) file;
    return (memory != NULL) ? (off_t) memory->read_position : (off_t) -1;
}

static void memory_message(struct mspack_file *file, const char *format, ...)
{
    (void) file;
    (void) format;
}

static void *memory_alloc(struct mspack_system *self, size_t bytes)
{
    (void) self;
    return malloc(bytes);
}

static void memory_free(void *ptr)
{
    free(ptr);
}

static void memory_copy(void *src, void *dest, size_t bytes)
{
    memcpy(dest, src, bytes);
}

static struct mspack_system g_memory_system = {
    &memory_open,
    &memory_close,
    &memory_read,
    &memory_write,
    &memory_seek,
    &memory_tell,
    &memory_message,
    &memory_alloc,
    &memory_free,
    &memory_copy,
    NULL
};

static int normalize_window_bits(int window_size)
{
    int window_bits = 0;

    if (window_size >= 32) {
        int size = window_size;
        while (size > 1) {
            size >>= 1;
            window_bits++;
        }

        return window_bits;
    }

    return window_size;
}

static int calculate_reset_interval(int partition_size)
{
    const int lzx_frame_size = 32768;

    if (partition_size <= 0) {
        return 0;
    }

    if ((partition_size % lzx_frame_size) != 0) {
        return 0;
    }

    return partition_size / lzx_frame_size;
}

__declspec(dllexport) int __cdecl console2lce_lzx_decompress(
    const uint8_t *compressed_buffer,
    int compressed_size,
    uint8_t *output_buffer,
    int output_buffer_size,
    int window_size,
    int partition_size,
    char *error_buffer,
    int error_buffer_size)
{
    struct lzxd_stream *stream = NULL;
    struct memory_file input = { 0 };
    struct memory_file output = { 0 };
    uint8_t *padded_input = NULL;
    int window_bits;
    int reset_interval;
    int result;

    if ((compressed_buffer == NULL) || (compressed_size <= 0)) {
        set_error(error_buffer, error_buffer_size, "Compressed buffer was empty.");
        return -1;
    }

    if ((output_buffer == NULL) || (output_buffer_size <= 0)) {
        set_error(error_buffer, error_buffer_size, "Output buffer was empty.");
        return -2;
    }

    window_bits = normalize_window_bits(window_size);
    if ((window_bits < 15) || (window_bits > 21)) {
        set_error(error_buffer, error_buffer_size, "Window size %d is outside the supported LZX range.", window_size);
        return -3;
    }

    if (partition_size <= 0) {
        set_error(error_buffer, error_buffer_size, "Compression partition size must be positive.");
        return -4;
    }

    reset_interval = calculate_reset_interval(partition_size);

    padded_input = (uint8_t *) calloc((size_t) compressed_size + CONSOLE2LCE_LZX_PADDING_BYTES, sizeof(uint8_t));
    if (padded_input == NULL) {
        set_error(error_buffer, error_buffer_size, "Failed to allocate padded input buffer.");
        return -5;
    }

    memcpy(padded_input, compressed_buffer, (size_t) compressed_size);

    input.read_buffer = padded_input;
    input.read_length = (size_t) compressed_size + CONSOLE2LCE_LZX_PADDING_BYTES;
    output.write_buffer = output_buffer;
    output.write_length = (size_t) output_buffer_size;

    stream = lzxd_init(
        &g_memory_system,
        (struct mspack_file *) &input,
        (struct mspack_file *) &output,
        window_bits,
        reset_interval,
        partition_size,
        (off_t) output_buffer_size,
        0);

    if (stream == NULL) {
        set_error(error_buffer, error_buffer_size, "lzxd_init failed for window=%d partition=%d.", window_bits, partition_size);
        free(padded_input);
        return -6;
    }

    result = lzxd_decompress(stream, (off_t) output_buffer_size);
    if (result != MSPACK_ERR_OK) {
        set_error(error_buffer, error_buffer_size, "lzxd_decompress failed with error %d.", result);
        lzxd_free(stream);
        free(padded_input);
        return -7;
    }

    lzxd_free(stream);
    free(padded_input);
    return (int) output.write_position;
}
