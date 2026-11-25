#include "li_internal.h"

#include <string.h>

#if defined(_M_IX86) || defined(_M_X64) || defined(__i386__) || defined(__x86_64__)
#define LI_X86_CPUID 1
#else
#define LI_X86_CPUID 0
#endif

#if LI_X86_CPUID
#if defined(_MSC_VER)
#include <intrin.h>
#else
#include <cpuid.h>
#endif

typedef struct cpuid_flag_descriptor {
    const char* name;
    uint8_t register_index;
    uint8_t bit;
} cpuid_flag_descriptor;

static void cpuid_query(uint32_t leaf, uint32_t subleaf, uint32_t registers[4])
{
#if defined(_MSC_VER)
    int values[4];
    __cpuidex(values, (int)leaf, (int)subleaf);
    registers[0] = (uint32_t)values[0];
    registers[1] = (uint32_t)values[1];
    registers[2] = (uint32_t)values[2];
    registers[3] = (uint32_t)values[3];
#else
    uint32_t eax = 0u;
    uint32_t ebx = 0u;
    uint32_t ecx = 0u;
    uint32_t edx = 0u;
    __cpuid_count(leaf, subleaf, eax, ebx, ecx, edx);
    registers[0] = eax;
    registers[1] = ebx;
    registers[2] = ecx;
    registers[3] = edx;
#endif
}

static void append_flag_object(li_json_builder* builder, const cpuid_flag_descriptor* flags, uintptr_t flag_count, const uint32_t registers[4])
{
    li_json_append_char(builder, '{');
    for (uintptr_t index = 0u; index < flag_count; ++index) {
        if (index != 0u) {
            li_json_append_char(builder, ',');
        }

        li_json_append_escaped(builder, flags[index].name);
        li_json_append_char(builder, ':');
        li_json_append_bool(builder, (registers[flags[index].register_index] & ((uint32_t)1u << flags[index].bit)) != 0u);
    }

    li_json_append_char(builder, '}');
}

static void append_leaf(li_json_builder* builder, uint32_t leaf, uint32_t subleaf, const uint32_t registers[4], int* first)
{
    if (!*first) {
        li_json_append_char(builder, ',');
    }

    *first = 0;
    li_json_append_format(builder,
        "{\"leaf\":\"0x%08lx\",\"subleaf\":\"0x%08lx\",\"eax\":\"0x%08lx\",\"ebx\":\"0x%08lx\",\"ecx\":\"0x%08lx\",\"edx\":\"0x%08lx\"}",
        (unsigned long)leaf,
        (unsigned long)subleaf,
        (unsigned long)registers[0],
        (unsigned long)registers[1],
        (unsigned long)registers[2],
        (unsigned long)registers[3]);
}

static void append_cache_leafs(li_json_builder* builder, uint32_t max_basic)
{
    li_json_append(builder, ",\"caches\":[");
    if (max_basic >= 4u) {
        int first = 1;
        for (uint32_t subleaf = 0u; subleaf < 32u; ++subleaf) {
            uint32_t registers[4];
            cpuid_query(4u, subleaf, registers);
            uint32_t cache_type = registers[0] & 0x1Fu;
            if (cache_type == 0u) {
                break;
            }

            uint32_t level = (registers[0] >> 5u) & 0x7u;
            uint32_t line_size = (registers[1] & 0xFFFu) + 1u;
            uint32_t partitions = ((registers[1] >> 12u) & 0x3FFu) + 1u;
            uint32_t ways = ((registers[1] >> 22u) & 0x3FFu) + 1u;
            uint32_t sets = registers[2] + 1u;
            uint64_t size_bytes = (uint64_t)line_size * partitions * ways * sets;
            if (!first) {
                li_json_append_char(builder, ',');
            }

            first = 0;
            li_json_append_format(builder,
                "{\"level\":%lu,\"type\":%lu,\"lineSizeBytes\":%lu,\"partitions\":%lu,\"ways\":%lu,\"sets\":%lu,\"sizeBytes\":%llu}",
                (unsigned long)level,
                (unsigned long)cache_type,
                (unsigned long)line_size,
                (unsigned long)partitions,
                (unsigned long)ways,
                (unsigned long)sets,
                (unsigned long long)size_bytes);
        }
    }

    li_json_append_char(builder, ']');
}

static void append_topology_leaf(li_json_builder* builder, uint32_t leaf)
{
    li_json_append(builder, ",\"topology\":[");
    int first = 1;
    for (uint32_t subleaf = 0u; subleaf < 16u; ++subleaf) {
        uint32_t registers[4];
        cpuid_query(leaf, subleaf, registers);
        uint32_t level_type = (registers[2] >> 8u) & 0xFFu;
        if (level_type == 0u) {
            break;
        }

        if (!first) {
            li_json_append_char(builder, ',');
        }

        first = 0;
        li_json_append_format(builder,
            "{\"leaf\":\"0x%08lx\",\"levelNumber\":%lu,\"levelType\":%lu,\"shift\":%lu,\"logicalProcessors\":%lu,\"x2ApicId\":%lu}",
            (unsigned long)leaf,
            (unsigned long)(registers[2] & 0xFFu),
            (unsigned long)level_type,
            (unsigned long)(registers[0] & 0x1Fu),
            (unsigned long)(registers[1] & 0xFFFFu),
            (unsigned long)registers[3]);
    }

    li_json_append_char(builder, ']');
}

static void append_raw_leafs(li_json_builder* builder, uint32_t max_basic, uint32_t max_extended)
{
    li_json_append(builder, ",\"rawLeaves\":[");
    int first = 1;
    uint32_t registers[4];
    for (uint32_t leaf = 0u; leaf <= max_basic && leaf <= 0x20u; ++leaf) {
        uint32_t subleaf_count = 1u;
        if (leaf == 4u || leaf == 7u || leaf == 0x0Bu || leaf == 0x1Fu) {
            subleaf_count = 4u;
        }

        for (uint32_t subleaf = 0u; subleaf < subleaf_count; ++subleaf) {
            cpuid_query(leaf, subleaf, registers);
            append_leaf(builder, leaf, subleaf, registers, &first);
        }
    }

    for (uint32_t leaf = 0x80000000u; leaf <= max_extended && leaf <= 0x80000008u; ++leaf) {
        cpuid_query(leaf, 0u, registers);
        append_leaf(builder, leaf, 0u, registers, &first);
    }

    li_json_append_char(builder, ']');
}
#endif

int32_t li_cpuid_to_json(li_json_output* output)
{
    if (output == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    output->data = NULL;
    output->length = 0u;
    li_json_builder builder;
    li_json_init(&builder);

#if !LI_X86_CPUID
    li_json_append(&builder, "{\"parser\":\"cpuid\",\"status\":\"unsupported\",\"reason\":\"CPUID доступен только на x86/x64.\"}");
    int32_t detach_status = li_json_detach(&builder, output);
    return detach_status == LI_STATUS_OK ? LI_STATUS_UNSUPPORTED : detach_status;
#else
    static const cpuid_flag_descriptor leaf1_flags[] = {
        { "sse3", 2u, 0u }, { "pclmulqdq", 2u, 1u }, { "dtes64", 2u, 2u }, { "monitor", 2u, 3u },
        { "dsCpl", 2u, 4u }, { "vmx", 2u, 5u }, { "smx", 2u, 6u }, { "eist", 2u, 7u },
        { "tm2", 2u, 8u }, { "ssse3", 2u, 9u }, { "cnxtId", 2u, 10u }, { "sdbg", 2u, 11u },
        { "fma", 2u, 12u }, { "cx16", 2u, 13u }, { "xtpr", 2u, 14u }, { "pdcm", 2u, 15u },
        { "pcid", 2u, 17u }, { "dca", 2u, 18u }, { "sse41", 2u, 19u }, { "sse42", 2u, 20u },
        { "x2apic", 2u, 21u }, { "movbe", 2u, 22u }, { "popcnt", 2u, 23u }, { "tscDeadline", 2u, 24u },
        { "aes", 2u, 25u }, { "xsave", 2u, 26u }, { "osxsave", 2u, 27u }, { "avx", 2u, 28u },
        { "f16c", 2u, 29u }, { "rdrand", 2u, 30u }, { "fpu", 3u, 0u }, { "vme", 3u, 1u },
        { "de", 3u, 2u }, { "pse", 3u, 3u }, { "tsc", 3u, 4u }, { "msr", 3u, 5u },
        { "pae", 3u, 6u }, { "mce", 3u, 7u }, { "cx8", 3u, 8u }, { "apic", 3u, 9u },
        { "sep", 3u, 11u }, { "mtrr", 3u, 12u }, { "pge", 3u, 13u }, { "mca", 3u, 14u },
        { "cmov", 3u, 15u }, { "pat", 3u, 16u }, { "pse36", 3u, 17u }, { "psn", 3u, 18u },
        { "clfsh", 3u, 19u }, { "ds", 3u, 21u }, { "acpi", 3u, 22u }, { "mmx", 3u, 23u },
        { "fxsr", 3u, 24u }, { "sse", 3u, 25u }, { "sse2", 3u, 26u }, { "htt", 3u, 28u },
        { "tm", 3u, 29u }, { "pbe", 3u, 31u }
    };
    static const cpuid_flag_descriptor leaf7_flags[] = {
        { "fsgsbase", 1u, 0u }, { "tscAdjust", 1u, 1u }, { "sgx", 1u, 2u }, { "bmi1", 1u, 3u },
        { "hle", 1u, 4u }, { "avx2", 1u, 5u }, { "fdpExcptnOnly", 1u, 6u }, { "smep", 1u, 7u },
        { "bmi2", 1u, 8u }, { "erms", 1u, 9u }, { "invpcid", 1u, 10u }, { "rtm", 1u, 11u },
        { "pqm", 1u, 12u }, { "deprecateFpuCsDs", 1u, 13u }, { "mpx", 1u, 14u }, { "pqe", 1u, 15u },
        { "avx512f", 1u, 16u }, { "avx512dq", 1u, 17u }, { "rdseed", 1u, 18u }, { "adx", 1u, 19u },
        { "smap", 1u, 20u }, { "avx512ifma", 1u, 21u }, { "clflushopt", 1u, 23u }, { "clwb", 1u, 24u },
        { "intelPt", 1u, 25u }, { "avx512pf", 1u, 26u }, { "avx512er", 1u, 27u }, { "avx512cd", 1u, 28u },
        { "sha", 1u, 29u }, { "avx512bw", 1u, 30u }, { "avx512vl", 1u, 31u }, { "prefetchwt1", 2u, 0u },
        { "avx512vbmi", 2u, 1u }, { "umip", 2u, 2u }, { "pku", 2u, 3u }, { "ospke", 2u, 4u },
        { "waitpkg", 2u, 5u }, { "avx512vbmi2", 2u, 6u }, { "cetSs", 2u, 7u }, { "gfni", 2u, 8u },
        { "vaes", 2u, 9u }, { "vpclmulqdq", 2u, 10u }, { "avx512vnni", 2u, 11u }, { "avx512bitalg", 2u, 12u },
        { "tme", 2u, 13u }, { "avx512vpopcntdq", 2u, 14u }, { "la57", 2u, 16u }, { "rdpid", 2u, 22u },
        { "kl", 2u, 23u }, { "busLockDetect", 2u, 24u }, { "cldemote", 2u, 25u }, { "movdiri", 2u, 27u },
        { "movdir64b", 2u, 28u }, { "enqcmd", 2u, 29u }, { "sgxLc", 2u, 30u }, { "pks", 2u, 31u },
        { "avx5124vnniw", 3u, 2u }, { "avx5124fmaps", 3u, 3u }, { "fsrm", 3u, 4u }, { "uintr", 3u, 5u },
        { "avx512Vp2Intersect", 3u, 8u }, { "srbdsCtrl", 3u, 9u }, { "mdClear", 3u, 10u }, { "rtmAlwaysAbort", 3u, 11u },
        { "tsxForceAbort", 3u, 13u }, { "serialize", 3u, 14u }, { "hybrid", 3u, 15u }, { "tsxldtrk", 3u, 16u },
        { "pconfig", 3u, 18u }, { "cetIbt", 3u, 20u }, { "amxBf16", 3u, 22u }, { "avx512Fp16", 3u, 23u },
        { "amxTile", 3u, 24u }, { "amxInt8", 3u, 25u }, { "ibrsIbpb", 3u, 26u }, { "stibp", 3u, 27u },
        { "l1dFlush", 3u, 28u }, { "archCaps", 3u, 29u }, { "coreCaps", 3u, 30u }, { "ssbd", 3u, 31u }
    };
    static const cpuid_flag_descriptor extended_flags[] = {
        { "lahfLm", 2u, 0u }, { "cmpLegacy", 2u, 1u }, { "svm", 2u, 2u }, { "extApic", 2u, 3u },
        { "cr8Legacy", 2u, 4u }, { "abm", 2u, 5u }, { "sse4a", 2u, 6u }, { "misalignSse", 2u, 7u },
        { "prefetchw", 2u, 8u }, { "osvw", 2u, 9u }, { "ibs", 2u, 10u }, { "xop", 2u, 11u },
        { "skinit", 2u, 12u }, { "wdt", 2u, 13u }, { "lwp", 2u, 15u }, { "fma4", 2u, 16u },
        { "tce", 2u, 17u }, { "nodeId", 2u, 19u }, { "tbm", 2u, 21u }, { "topoext", 2u, 22u },
        { "perfctrCore", 2u, 23u }, { "perfctrNb", 2u, 24u }, { "bpext", 2u, 26u }, { "ptsc", 2u, 27u },
        { "perfctrLlc", 2u, 28u }, { "mwaitx", 2u, 29u }, { "syscall", 3u, 11u }, { "nx", 3u, 20u },
        { "mmxExt", 3u, 22u }, { "fxsrOpt", 3u, 25u }, { "pdpe1gb", 3u, 26u }, { "rdtscp", 3u, 27u },
        { "lm", 3u, 29u }, { "amd3dNowExt", 3u, 30u }, { "amd3dNow", 3u, 31u }
    };

    uint32_t registers[4];
    cpuid_query(0u, 0u, registers);
    uint32_t max_basic = registers[0];
    char vendor[13];
    memcpy(vendor, &registers[1], 4u);
    memcpy(vendor + 4u, &registers[3], 4u);
    memcpy(vendor + 8u, &registers[2], 4u);
    vendor[12] = '\0';

    uint32_t extended_registers[4];
    cpuid_query(0x80000000u, 0u, extended_registers);
    uint32_t max_extended = extended_registers[0];

    li_json_append(&builder, "{\"parser\":\"cpuid\",\"status\":\"ok\"");
    li_json_append(&builder, ",\"vendor\":");
    li_json_append_escaped(&builder, vendor);
    li_json_append_format(&builder, ",\"maxBasicLeaf\":\"0x%08lx\",\"maxExtendedLeaf\":\"0x%08lx\"", (unsigned long)max_basic, (unsigned long)max_extended);

    if (max_basic >= 1u) {
        uint32_t leaf1[4];
        cpuid_query(1u, 0u, leaf1);
        uint32_t eax = leaf1[0];
        uint32_t stepping = eax & 0x0Fu;
        uint32_t base_model = (eax >> 4u) & 0x0Fu;
        uint32_t base_family = (eax >> 8u) & 0x0Fu;
        uint32_t processor_type = (eax >> 12u) & 0x03u;
        uint32_t extended_model = (eax >> 16u) & 0x0Fu;
        uint32_t extended_family = (eax >> 20u) & 0xFFu;
        uint32_t display_family = base_family == 0x0Fu ? base_family + extended_family : base_family;
        uint32_t display_model = (base_family == 0x06u || base_family == 0x0Fu) ? (extended_model << 4u) + base_model : base_model;
        li_json_append_format(&builder, ",\"family\":%lu,\"model\":%lu,\"stepping\":%lu,\"processorType\":%lu", (unsigned long)display_family, (unsigned long)display_model, (unsigned long)stepping, (unsigned long)processor_type);
        li_json_append(&builder, ",\"leaf1Flags\":");
        append_flag_object(&builder, leaf1_flags, LI_ARRAY_LEN(leaf1_flags), leaf1);
        li_json_append_format(&builder, ",\"initialApicId\":%lu,\"logicalProcessorCountLeaf1\":%lu", (unsigned long)((leaf1[1] >> 24u) & 0xFFu), (unsigned long)((leaf1[1] >> 16u) & 0xFFu));
    }

    if (max_basic >= 7u) {
        uint32_t leaf7[4];
        cpuid_query(7u, 0u, leaf7);
        li_json_append(&builder, ",\"leaf7Flags\":");
        append_flag_object(&builder, leaf7_flags, LI_ARRAY_LEN(leaf7_flags), leaf7);
    }

    if (max_extended >= 0x80000001u) {
        uint32_t leaf_ext[4];
        cpuid_query(0x80000001u, 0u, leaf_ext);
        li_json_append(&builder, ",\"extendedFlags\":");
        append_flag_object(&builder, extended_flags, LI_ARRAY_LEN(extended_flags), leaf_ext);
    }

    if (max_extended >= 0x80000004u) {
        char brand[49];
        uint32_t brand_leaf[4];
        for (uint32_t leaf = 0u; leaf < 3u; ++leaf) {
            cpuid_query(0x80000002u + leaf, 0u, brand_leaf);
            memcpy(brand + (leaf * 16u), brand_leaf, 16u);
        }

        brand[48] = '\0';
        for (uintptr_t index = 47u; index > 0u && brand[index] == ' '; --index) {
            brand[index] = '\0';
        }

        li_json_append(&builder, ",\"brandString\":");
        li_json_append_escaped(&builder, brand);
    }

    if (max_basic >= 0x1Au) {
        uint32_t hybrid[4];
        cpuid_query(0x1Au, 0u, hybrid);
        li_json_append_format(&builder, ",\"hybridCoreType\":%lu", (unsigned long)((hybrid[0] >> 24u) & 0xFFu));
    }

    append_cache_leafs(&builder, max_basic);
    if (max_basic >= 0x1Fu) {
        append_topology_leaf(&builder, 0x1Fu);
    }
    else if (max_basic >= 0x0Bu) {
        append_topology_leaf(&builder, 0x0Bu);
    }
    else {
        li_json_append(&builder, ",\"topology\":[]");
    }

    append_raw_leafs(&builder, max_basic, max_extended);
    li_json_append_char(&builder, '}');

    int32_t status = li_json_detach(&builder, output);
    return status;
#endif
}
