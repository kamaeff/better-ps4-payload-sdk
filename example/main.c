#include <stdarg.h>
#include "../ps4-libjbc/jailbreak.h"
#include "struct.h"

// Send notification using OSM-Made's method
// which allows using icon other than the football one
// Original: https://github.com/OSM-Made/PS4-Notify
// This example shows how to dynamically load PS4 libs (sceKernelSendNotificationRequest)
// and libc (vsnprintf, strlcpy, printf)

// To read stdout, enable 'stdout to klog' in GoldHEN 'servers' settings
// and run netcat (assuming your PS4 IP address is 192.168.100.100):
// nc 192.168.100.100 3232
// To easily send the payload to the PS4, enable Payloader server in GoldHEN
// and use netcat, e.g.:
// make && nc -q0 192.168.100.100 9090 < payload.bin
// Note that different netcat implementations use different cli options


int (*vsnprintf)(char* str, size_t size, const char *format, va_list);
int (*printf)(const char *format, ...);
size_t (*strlcpy)(char* dst, char* src, size_t dst_size);

int (*sceKernelSendNotificationRequest)(int, SceNotificationRequest* req, size_t size, int blocking);

void init_libs() {
	void* libc = dlopen("libSceLibcInternal.sprx", 0);
	vsnprintf = dlsym(libc, "vsnprintf");
	strlcpy = dlsym(libc, "strlcpy");
	printf = dlsym(libc, "printf");

	void* kernel = dlopen("libkernel.sprx", 0);
	if (!kernel) {
		kernel = dlopen("libkernel_web.sprx", 0);
	}
	if (!kernel) {
		kernel = dlopen("libkernel_sys.sprx", 0);
	}
	sceKernelSendNotificationRequest = dlsym(kernel, "sceKernelSendNotificationRequest");
}

#define notify_bufsize 0x400
#define notify_bufsize_icon 0x800
void printf_notify(char* fmt, ...) {
	SceNotificationRequest req = {};

	va_list args;
	va_start(args, fmt);
	vsnprintf(req.Message, notify_bufsize, fmt, args);
	va_end(args);

	req.Type = 0;
	req.Attribute = 0;
	req.HasIcon = 1;
	req.TargetId = -1;

	strlcpy(req.IconImageUri, "cxml://psnotification/tex_default_icon_smaps", notify_bufsize_icon);
	
	printf("[DEBUG] printf_notify: %s\n", req.Message);
	sceKernelSendNotificationRequest(0, (SceNotificationRequest *)&req, sizeof(SceNotificationRequest), 0);
}

// all the stuff commented out is probably needed for sandbox escaping
// but idk for me everything was working without it; maybe it's for PKGs only
// and payloads loaded through GoldHEN payloader are already fine
// Or maybe the sandbox is just for filesystem io, which I don't use
// anyway keeping this here (and libjbc itself too) in case it would be useful for anyone

// asm("clear_stack:\nmov $0x800,%ecx\nxor %rax, %rax\n.L1:\npush %rax\nloop .L1\nadd $0x4000,%rsp\nret");
// void clear_stack(void);


int main()
{
	// struct jbc_cred cred;
	// jbc_get_cred(&cred);
	// jbc_jailbreak_cred(&cred);

	// cred.jdir = 0;
	// cred.sceProcType = 0x3800000000000010;
	// cred.sonyCred = 0x40001c0000000000;
	// cred.sceProcCap = 0x900000000000ff00;
	// jbc_set_cred(&cred);

	// clear_stack();

	init_libs();

	printf_notify(
		"Hello from the %s!\nWe're using %s to send notification with custom icon and %s for string formatting.",
		"example", "sceKernelSendNotificationRequest", "vsnprintf"
	);

	return 0;
}
