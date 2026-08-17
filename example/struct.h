#ifndef DEFPARAMS
#define DEFPARAMS

#pragma pack(push, 1)
typedef struct SceNotificationRequest
{
	uint32_t Type;   						// 0x00
	uint32_t ReqId;            				// 0x04
	uint32_t Priority;						// 0x08
	uint32_t MsgId;            				// 0x0C
	uint32_t TargetId;						// 0x10
	uint32_t UserId;						// 0x14
	uint32_t DeviceId;						// 0x18
	uint32_t AddressingUserId;				// 0x1C
	uint32_t AppId;            				// 0x20
	uint32_t ErrorNumber;					// 0x24
	uint32_t Attribute;						// 0x28
	uint8_t  HasIcon;						// 0x2C
	char Message[0x400];					// 0x2D
	char IconImageUri[0x800];				// 0x42D
	char _padding[0x3];						// 0xC2D
} SceNotificationRequest;
#pragma pack(pop)
// static_assert(sizeof(SceNotificationRequest) == 0xC30, "Size of SceNotificationRequest is not 0xC30");

void* dlopen(const char*, int);
void* dlsym(void*, const char*);

#endif
