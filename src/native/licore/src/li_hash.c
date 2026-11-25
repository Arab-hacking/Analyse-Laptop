#include "li_internal.h"

#include <stddef.h>
#include <stdint.h>
#include <string.h>

typedef struct li_sha256_context {
    uint32_t state[8];
    uint64_t bit_length;
    uint8_t buffer[64];
    uintptr_t buffer_length;
} li_sha256_context;

static const uint32_t li_sha256_k[64] = {
    0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u,
    0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
    0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u,
    0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
    0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu,
    0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
    0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u,
    0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
    0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u,
    0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
    0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u,
    0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
    0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u,
    0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
    0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u,
    0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u
};

static uint32_t rotr32(uint32_t value, uint32_t bits)
{
    return (value >> bits) | (value << (32u - bits));
}

static uint32_t load_be32(const uint8_t* data)
{
    return ((uint32_t)data[0] << 24u) |
        ((uint32_t)data[1] << 16u) |
        ((uint32_t)data[2] << 8u) |
        (uint32_t)data[3];
}

static void store_be32(uint8_t* data, uint32_t value)
{
    data[0] = (uint8_t)(value >> 24u);
    data[1] = (uint8_t)(value >> 16u);
    data[2] = (uint8_t)(value >> 8u);
    data[3] = (uint8_t)value;
}

static void store_be64(uint8_t* data, uint64_t value)
{
    data[0] = (uint8_t)(value >> 56u);
    data[1] = (uint8_t)(value >> 48u);
    data[2] = (uint8_t)(value >> 40u);
    data[3] = (uint8_t)(value >> 32u);
    data[4] = (uint8_t)(value >> 24u);
    data[5] = (uint8_t)(value >> 16u);
    data[6] = (uint8_t)(value >> 8u);
    data[7] = (uint8_t)value;
}

static void sha256_transform(li_sha256_context* context, const uint8_t block[64])
{
    uint32_t schedule[64];
    for (uint32_t index = 0u; index < 16u; ++index) {
        schedule[index] = load_be32(block + (index * 4u));
    }

    for (uint32_t index = 16u; index < 64u; ++index) {
        uint32_t s0 = rotr32(schedule[index - 15u], 7u) ^ rotr32(schedule[index - 15u], 18u) ^ (schedule[index - 15u] >> 3u);
        uint32_t s1 = rotr32(schedule[index - 2u], 17u) ^ rotr32(schedule[index - 2u], 19u) ^ (schedule[index - 2u] >> 10u);
        schedule[index] = schedule[index - 16u] + s0 + schedule[index - 7u] + s1;
    }

    uint32_t a = context->state[0];
    uint32_t b = context->state[1];
    uint32_t c = context->state[2];
    uint32_t d = context->state[3];
    uint32_t e = context->state[4];
    uint32_t f = context->state[5];
    uint32_t g = context->state[6];
    uint32_t h = context->state[7];

    for (uint32_t index = 0u; index < 64u; ++index) {
        uint32_t s1 = rotr32(e, 6u) ^ rotr32(e, 11u) ^ rotr32(e, 25u);
        uint32_t ch = (e & f) ^ ((~e) & g);
        uint32_t temp1 = h + s1 + ch + li_sha256_k[index] + schedule[index];
        uint32_t s0 = rotr32(a, 2u) ^ rotr32(a, 13u) ^ rotr32(a, 22u);
        uint32_t maj = (a & b) ^ (a & c) ^ (b & c);
        uint32_t temp2 = s0 + maj;
        h = g;
        g = f;
        f = e;
        e = d + temp1;
        d = c;
        c = b;
        b = a;
        a = temp1 + temp2;
    }

    context->state[0] += a;
    context->state[1] += b;
    context->state[2] += c;
    context->state[3] += d;
    context->state[4] += e;
    context->state[5] += f;
    context->state[6] += g;
    context->state[7] += h;
}

static void sha256_init(li_sha256_context* context)
{
    context->state[0] = 0x6a09e667u;
    context->state[1] = 0xbb67ae85u;
    context->state[2] = 0x3c6ef372u;
    context->state[3] = 0xa54ff53au;
    context->state[4] = 0x510e527fu;
    context->state[5] = 0x9b05688cu;
    context->state[6] = 0x1f83d9abu;
    context->state[7] = 0x5be0cd19u;
    context->bit_length = 0u;
    context->buffer_length = 0u;
}

static void sha256_update(li_sha256_context* context, const uint8_t* data, uintptr_t length)
{
    for (uintptr_t index = 0u; index < length; ++index) {
        context->buffer[context->buffer_length++] = data[index];
        if (context->buffer_length == sizeof(context->buffer)) {
            sha256_transform(context, context->buffer);
            context->bit_length += 512u;
            context->buffer_length = 0u;
        }
    }
}

static void sha256_final(li_sha256_context* context, uint8_t digest[32])
{
    uintptr_t index = context->buffer_length;
    context->buffer[index++] = 0x80u;

    if (index > 56u) {
        while (index < 64u) {
            context->buffer[index++] = 0u;
        }

        sha256_transform(context, context->buffer);
        index = 0u;
    }

    while (index < 56u) {
        context->buffer[index++] = 0u;
    }

    context->bit_length += (uint64_t)context->buffer_length * 8u;
    store_be64(context->buffer + 56u, context->bit_length);
    sha256_transform(context, context->buffer);

    for (uint32_t state_index = 0u; state_index < 8u; ++state_index) {
        store_be32(digest + (state_index * 4u), context->state[state_index]);
    }
}

int32_t li_core_sha256_hex(const uint8_t* data, uintptr_t length, char output_hex[65])
{
    static const char digits[] = "0123456789abcdef";
    if ((data == NULL && length != 0u) || output_hex == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    li_sha256_context context;
    uint8_t digest[32];
    sha256_init(&context);
    if (length != 0u) {
        sha256_update(&context, data, length);
    }

    sha256_final(&context, digest);

    for (uintptr_t index = 0u; index < sizeof(digest); ++index) {
        output_hex[index * 2u] = digits[(digest[index] >> 4u) & 0x0Fu];
        output_hex[(index * 2u) + 1u] = digits[digest[index] & 0x0Fu];
    }

    output_hex[64] = '\0';
    return LI_STATUS_OK;
}
