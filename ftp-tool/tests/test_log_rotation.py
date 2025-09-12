from utils import logger
import time

# Configure logger with small size for quick test
logger.configure_logging({'EnableFileLogging': True, 'MaxLogSize': 1, 'LogLevel': 'INFO'})
log = logger.get_logger()

# write many messages to exceed 1 MB quickly
for i in range(20000):
    log.info('Test message %d %s' % (i, 'x'*200))
    if i % 1000 == 0:
        print('Wrote', i)
    time.sleep(0.0001)
print('Done')
